using System.Text.Json;
using System.Threading.Channels;

namespace gflat.LanguageServer;

/// <summary>One event loop owns documents and stdout. Analysis runs serially in the background.</summary>
public sealed partial class LanguageServer(Stream input, Stream output, TextWriter log)
{
    private abstract record Event;
    private sealed record Message(byte[] Body) : Event;
    private sealed record Finished(long Generation, Analysis Result, IReadOnlyDictionary<string, OpenDocument> Documents, long ProjectRevision) : Event;
    private sealed record Changed(string Path, bool Force = false) : Event;
    private sealed record InputEnded(Exception? Error) : Event;
    private readonly Channel<Event> events = Channel.CreateUnbounded<Event>(new() { SingleReader = true });
    private readonly Dictionary<string, OpenDocument> documents = new(Workspace.Paths);
    private readonly HashSet<string> watchedPaths = new(Workspace.Paths);
    private readonly HashSet<string> projectDirectories = new(Workspace.Paths);
    private readonly HashSet<string> projectDiscoveryDirectories = new(Workspace.Paths);
    private readonly ProjectWorkspace projects = new();
    private readonly List<FileSystemWatcher> watchers = new();
    private HashSet<string> watcherInputs = new(Workspace.Paths);
    private Dictionary<string, bool> watcherDirectoryInputs = new(Workspace.Paths);
    private volatile bool watchersNeedRefresh;
    private readonly SemaphoreSlim analysisGate = new(1, 1);
    private CancellationTokenSource? pending;
    private long generation;
    private long projectRevision, evaluatedProjectRevision = -1;
    private string? root;
    private bool initialized, shutdown;
    private Analysis? latest;
    private IReadOnlyDictionary<string, OpenDocument>? latestDocuments;
    private long latestProjectRevision = -1;
    private long analyzedGeneration = -1;
    private long fullyAnalyzedGeneration = -1;
    private TaskCompletionSource<bool>? interactiveRequested;
    private TaskCompletionSource<bool>? fullAnalysisRequested;
    private sealed record EditorRequest(JsonElement Id, string Method, JsonElement Parameters, long Generation);
    private readonly List<EditorRequest> editorRequests = [];
    private string[] tokenTypes = EditorModel.TokenTypes;
    private VisualStudioHover? hoverPresentation;
    private HoverOptions hoverOptions = new();
    private bool versionedWorkspaceEdits;
    private bool hierarchicalDocumentSymbols;
    private bool bracesOnNewLine = true;
    private readonly Dictionary<string, string> selectedHeaderContexts = new(Workspace.Paths);
    private readonly HashSet<string> pendingClientRequests = new();
    private bool semanticRefreshSupported;
    private bool visualStudioDocumentOpen;
    private bool showDocumentSupported;

    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _ = ReadMessages(lifetime.Token);
        try
        {
            await foreach (var item in events.Reader.ReadAllAsync(lifetime.Token))
            {
                switch (item)
                {
                    case InputEnded ended:
                        if (ended.Error != null) await log.WriteLineAsync(ended.Error.ToString());
                        return shutdown && ended.Error == null ? 0 : 1;
                    case Message message:
                        int? exit = await Handle(message.Body);
                        if (exit != null) return exit.Value;
                        break;
                    case Changed changed when !shutdown:
                        if (changed.Force || RelevantFileChange(changed.Path))
                        { await Schedule(invalidateProjects: true); }
                        break;
                    case Finished result when result.Generation == generation && !shutdown:
                        latest = result.Result;
                        if (latest.Models != null)
                            foreach (var selection in selectedHeaderContexts)
                                if (latest.Models.GetValueOrDefault(selection.Key) is { } model && model.HeaderContexts(selection.Key).Any(c => c.Id == selection.Value))
                                    latest.Models[selection.Key] = model.WithHeaderContext(selection.Key, selection.Value);
                        latestDocuments = result.Documents;
                        latestProjectRevision = result.ProjectRevision;
                        analyzedGeneration = result.Generation;
                        if (!result.Result.IsPartial) fullyAnalyzedGeneration = result.Generation;
                        watchedPaths.Clear();
                        watchedPaths.UnionWith(result.Result.WatchedPaths);
                        projectDirectories.Clear();
                        projectDirectories.UnionWith(result.Result.ProjectDirectories ?? []);
                        projectDiscoveryDirectories.Clear();
                        projectDiscoveryDirectories.UnionWith(result.Result.ProjectDiscoveryDirectories ?? []);
                        UpdateWatchers();
                        foreach (var query in editorRequests.Where(query => !RequiresFullAnalysis(query.Method) || fullyAnalyzedGeneration == generation)
                            .OrderBy(query => QueryPriority(query.Method)).ToArray())
                        {
                            editorRequests.Remove(query);
                            await AnswerEditor(query);
                        }
                        await PublishDiagnostics(result.Result);
                        break;
                }
            }
            return 1;
        }
        finally
        {
            lifetime.Cancel();
            pending?.Cancel();
            foreach (var watcher in watchers) watcher.Dispose();
            events.Writer.TryComplete();
        }
    }

    private async Task ReadMessages(CancellationToken cancellation)
    {
        Exception? failure = null;
        try
        {
            while (await Protocol.ReadAsync(input, cancellation) is byte[] body)
                await events.Writer.WriteAsync(new Message(body), cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        catch (Exception error) { failure = error; }
        events.Writer.TryWrite(new InputEnded(failure));
    }

    private Task Send(object value) => Protocol.WriteAsync(output, value);
    // Result and id must retain JSON null, even though optional notification fields omit it.
    private Task Reply(JsonElement id, object? result) => Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
    private Task Error(object? id, int code, string message) => Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new { code, message } });

    private async Task<int?> Handle(byte[] body)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(body); }
        catch (JsonException) { await Error(null, -32700, "Invalid JSON"); return null; }
        using (json)
        {
            var request = json.RootElement;
            if (request.ValueKind == JsonValueKind.Object && !request.TryGetProperty("method", out _) &&
                request.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.String &&
                pendingClientRequests.Remove(responseId.GetString()!)) return null;
            if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                await Error(null, -32600, "Expected a JSON-RPC request");
                return null;
            }
            bool hasId = request.TryGetProperty("id", out var id);
            string method = methodElement.GetString()!;
            if (!request.TryGetProperty("jsonrpc", out var rpc) || rpc.ValueKind != JsonValueKind.String || rpc.GetString() != "2.0" ||
                hasId && id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String or JsonValueKind.Null) ||
                !hasId && method is "initialize" or "shutdown")
            {
                await Error(null, -32600, "Invalid JSON-RPC request");
                return null;
            }
            if (method == "exit") return shutdown ? 0 : 1;
            if (shutdown) { if (hasId) await Error(id, -32600, "Server has shut down"); return null; }
            if (!initialized && method != "initialize") { if (hasId) await Error(id, -32002, "Server is not initialized"); return null; }
            request.TryGetProperty("params", out var parameters);
            try
            {
                switch (method)
                {
                    case "initialize":
                        if (initialized) { await Error(id, -32600, "Server is already initialized"); break; }
                        if (parameters.TryGetProperty("rootUri", out var rootUri) && rootUri.ValueKind == JsonValueKind.String)
                            root = Workspace.FilePath(rootUri.GetString()!);
                        else if (parameters.TryGetProperty("workspaceFolders", out var folders) && folders.ValueKind == JsonValueKind.Array && folders.GetArrayLength() > 0)
                            root = Workspace.FilePath(folders[0].GetProperty("uri").GetString()!);
                        initialized = true;
                        hierarchicalDocumentSymbols = parameters.TryGetProperty("capabilities", out var capabilities) &&
                            capabilities.TryGetProperty("textDocument", out var textDocumentCapabilities) &&
                            textDocumentCapabilities.TryGetProperty("documentSymbol", out var symbolCapabilities) &&
                            symbolCapabilities.TryGetProperty("hierarchicalDocumentSymbolSupport", out var hierarchy) && hierarchy.ValueKind == JsonValueKind.True;
                        versionedWorkspaceEdits = capabilities.ValueKind == JsonValueKind.Object &&
                            capabilities.TryGetProperty("workspace", out var workspaceCapabilities) &&
                            workspaceCapabilities.TryGetProperty("workspaceEdit", out var editCapabilities) &&
                            editCapabilities.TryGetProperty("documentChanges", out var documentChanges) && documentChanges.ValueKind == JsonValueKind.True;
                        semanticRefreshSupported = capabilities.ValueKind == JsonValueKind.Object &&
                            capabilities.TryGetProperty("workspace", out var refreshWorkspace) && refreshWorkspace.TryGetProperty("semanticTokens", out var refreshTokens) &&
                            refreshTokens.TryGetProperty("refreshSupport", out var refreshSupport) && refreshSupport.ValueKind == JsonValueKind.True;
                        showDocumentSupported = capabilities.ValueKind == JsonValueKind.Object && capabilities.TryGetProperty("window", out var windowCapabilities) &&
                            windowCapabilities.TryGetProperty("showDocument", out var showDocument) && showDocument.TryGetProperty("support", out var showSupport) && showSupport.ValueKind == JsonValueKind.True;
                        if (parameters.TryGetProperty("initializationOptions", out var options) && options.ValueKind == JsonValueKind.Object &&
                            options.TryGetProperty("visualStudioClassifications", out var vsColors) && vsColors.ValueKind == JsonValueKind.True)
                            tokenTypes = EditorModel.VisualStudioTokenTypes;
                        if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("hoverIcons", out var hoverIcons))
                            hoverPresentation = new VisualStudioHover(hoverIcons);
                        hoverOptions = hoverOptions.Update(options);
                        visualStudioDocumentOpen = options.ValueKind == JsonValueKind.Object && options.TryGetProperty("visualStudioDocumentOpen", out var nativeOpen) && nativeOpen.ValueKind == JsonValueKind.True;
                        UpdateFormattingOptions(options);
                        await Reply(id, new { capabilities = new {
                            positionEncoding = "utf-16",
                            textDocumentSync = new { openClose = true, change = 1, save = new { includeText = true } },
                            completionProvider = new { triggerCharacters = new[] { ".", ":", "/", "\\", "\"", "<" } },
                            hoverProvider = !(options.ValueKind == JsonValueKind.Object && options.TryGetProperty("visualStudioNativeHover", out var nativeHover) && nativeHover.ValueKind == JsonValueKind.True), definitionProvider = true, referencesProvider = true, documentHighlightProvider = true,
                            renameProvider = new { prepareProvider = true }, workspaceSymbolProvider = true,
                            implementationProvider = true, typeHierarchyProvider = true,
                            documentFormattingProvider = true, documentRangeFormattingProvider = true,
                            codeActionProvider = new { codeActionKinds = new[] { "quickfix", "refactor.rewrite", "refactor.inline", "source" } },
                            executeCommandProvider = new { commands = new[] { "gflat.selectHeaderContext", "gflat.inspectMacro", "gflat.generateDocumentation" } },
                            documentOnTypeFormattingProvider = new { firstTriggerCharacter = "}", moreTriggerCharacter = new[] { ";" } },
                            signatureHelpProvider = new { triggerCharacters = new[] { "(", "," }, retriggerCharacters = new[] { ")" } }, documentSymbolProvider = true,
                            semanticTokensProvider = new { legend = new { tokenTypes, tokenModifiers = EditorModel.TokenModifiers }, full = true, range = true }
                        }, serverInfo = new { name = "gflat", version = "0.2.0" } });
                        break;
                    case "workspace/didChangeConfiguration":
                        if (parameters.TryGetProperty("settings", out var settings))
                        {
                            hoverOptions = hoverOptions.Update(settings);
                            UpdateFormattingOptions(settings);
                        }
                        break;
                    case "initialized": await Schedule(); break;
                    case "shutdown":
                        shutdown = true;
                        pending?.Cancel();
                        generation++;
                        await Reply(id, null);
                        break;
                    case "textDocument/didOpen":
                    {
                        var document = parameters.GetProperty("textDocument");
                        string path = Workspace.FilePath(document.GetProperty("uri").GetString()!);
                        documents[path] = new(path, document.GetProperty("text").GetString()!, document.GetProperty("version").GetInt32());
                        // Loose-file sessions can still opt into a workspace in that file's directory.
                        root ??= Path.GetDirectoryName(path);
                        await Schedule();
                        break;
                    }
                    case "textDocument/didChange":
                    {
                        var document = parameters.GetProperty("textDocument");
                        string path = Workspace.FilePath(document.GetProperty("uri").GetString()!);
                        int version = document.GetProperty("version").GetInt32();
                        if (!documents.TryGetValue(path, out var previous) || version <= previous.Version) break;
                        string text = Workspace.ApplyChanges(previous.Text, parameters.GetProperty("contentChanges"));
                        documents[path] = new(path, text, version);
                        await Schedule();
                        break;
                    }
                    case "textDocument/didSave":
                    {
                        string path = Workspace.FilePath(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
                        if (documents.TryGetValue(path, out var previous) && parameters.TryGetProperty("text", out var text))
                            documents[path] = previous with { Text = text.GetString()! };
                        await Schedule();
                        break;
                    }
                    case "textDocument/didClose":
                        documents.Remove(Workspace.FilePath(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!));
                        await Schedule();
                        break;
                    case "workspace/didChangeWatchedFiles":
                        if (!parameters.TryGetProperty("changes", out var changes) || changes.EnumerateArray().Any(change =>
                            RelevantFileChange(Workspace.FilePath(change.GetProperty("uri").GetString()!))))
                            await Schedule(invalidateProjects: true);
                        break;
                    case "$/cancelRequest":
                        if (parameters.TryGetProperty("id", out var canceled))
                        {
                            var query = editorRequests.FirstOrDefault(q => q.Id.GetRawText() == canceled.GetRawText());
                            if (query != null) { editorRequests.Remove(query); await Error(query.Id, -32800, "Request cancelled"); }
                        }
                        break;
                    case "$/setTrace": break;
                    case "textDocument/completion":
                    case "textDocument/hover":
                    case "textDocument/definition":
                    case "workspace/executeCommand":
                    case "textDocument/codeAction":
                    case "textDocument/formatting":
                    case "textDocument/rangeFormatting":
                    case "textDocument/onTypeFormatting":
                    case "textDocument/implementation":
                    case "textDocument/prepareTypeHierarchy":
                    case "typeHierarchy/supertypes":
                    case "typeHierarchy/subtypes":
                    case "textDocument/references":
                    case "textDocument/documentHighlight":
                    case "textDocument/prepareRename":
                    case "textDocument/rename":
                    case "workspace/symbol":
                    case "textDocument/documentSymbol":
                    case "textDocument/signatureHelp":
                    case "textDocument/semanticTokens/full":
                    case "textDocument/semanticTokens/range":
                        if (hasId)
                        {
                            var query = new EditorRequest(id.Clone(), method, parameters.Clone(), generation);
                            if (IsFormattingRequest(method)) await AnswerEditor(query);
                            else if (method == "textDocument/completion" && analyzedGeneration != generation && TryReuseMemberCompletion(query, out var reused)) await Reply(query.Id, reused);
                            else if (analyzedGeneration == generation && (!RequiresFullAnalysis(method) || fullyAnalyzedGeneration == generation)) await AnswerEditor(query);
                            else
                            {
                                editorRequests.Add(query);
                                interactiveRequested?.TrySetResult(true);
                                if (RequiresFullAnalysis(method)) fullAnalysisRequested?.TrySetResult(true);
                            }
                        }
                        break;
                    default: if (hasId) await Error(id, -32601, "Method not supported: " + method); break;
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException)
            {
                if (hasId) await Error(id, -32602, error.Message);
                else await Send(new { jsonrpc = "2.0", method = "window/logMessage", @params = new { type = 1, message = "Invalid " + method + ": " + error.Message } });
            }
        }
        return null;
    }

    private async Task AnswerEditor(EditorRequest query)
    {
        if (query.Generation != generation) { await Error(query.Id, -32801, "Document changed"); return; }
        try
        {
            if (query.Method == "workspace/executeCommand")
            {
                string command = query.Parameters.GetProperty("command").GetString()!;
                var arguments = query.Parameters.GetProperty("arguments");
                string commandPath = Workspace.FilePath(arguments[0].GetString()!);
                if (command == "gflat.generateDocumentation" && latest?.Models?.GetValueOrDefault(commandPath) is { } documentationModel)
                {
                    string directory = arguments.GetArrayLength() > 1 ? Path.GetFullPath(arguments[1].GetString()!) :
                        Path.Combine(Path.GetTempPath(), "gflat", "manuals", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);
                    var entries = documentationModel.ExportDocumentation();
                    await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), DocumentationManual.Html(entries));
                    await File.WriteAllTextAsync(Path.Combine(directory, "documentation.json"), JsonSerializer.Serialize(new { version = 1, entries }, Protocol.JsonOptions));
                    await Reply(query.Id, new { uri = Workspace.FileUri(Path.Combine(directory, "index.html")), directory });
                    await Send(new { jsonrpc = "2.0", method = "window/showMessage", @params = new { type = 3, message = "Documentation generated: " + Path.Combine(directory, "index.html") } });
                    return;
                }
                if (command == "gflat.selectHeaderContext" && latest?.Models?.GetValueOrDefault(commandPath) is { } headerModel)
                {
                    string context = arguments[1].GetString()!;
                    latest.Models[commandPath] = headerModel.WithHeaderContext(commandPath, context);
                    selectedHeaderContexts[commandPath] = context;
                    await Reply(query.Id, null);
                    if (semanticRefreshSupported)
                    {
                        string refreshId = "gflat-header-refresh-" + Guid.NewGuid().ToString("N");
                        pendingClientRequests.Add(refreshId);
                        await Send(new { jsonrpc = "2.0", id = refreshId, method = "workspace/semanticTokens/refresh", @params = new { } });
                    }
                    return;
                }
                if (command == "gflat.inspectMacro" && latest?.Models?.GetValueOrDefault(commandPath)?.MacroInspection(commandPath, arguments[1].GetInt32()) is { } inspection)
                {
                    await Reply(query.Id, inspection);
                    if (visualStudioDocumentOpen || showDocumentSupported)
                    {
                        string directory = Path.Combine(Path.GetTempPath(), "gflat", "macro-expansions");
                        Directory.CreateDirectory(directory);
                        string file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt");
                        await File.WriteAllTextAsync(file, inspection);
                        if (visualStudioDocumentOpen)
                            await Send(new { jsonrpc = "2.0", method = "gflat/showDocument", @params = new { uri = Workspace.FileUri(file) } });
                        else
                        {
                            string showId = "gflat-document-" + Guid.NewGuid().ToString("N");
                            pendingClientRequests.Add(showId);
                            await Send(new { jsonrpc = "2.0", id = showId, method = "window/showDocument", @params = new { uri = Workspace.FileUri(file), takeFocus = true } });
                        }
                    }
                    else await Send(new { jsonrpc = "2.0", method = "window/showMessage", @params = new { type = 3, message = inspection } });
                    return;
                }
                throw new ArgumentException("The requested editor command is unavailable.");
            }
            if (query.Method == "workspace/symbol")
            {
                string search = query.Parameters.GetProperty("query").GetString() ?? throw new ArgumentException("query must be a string");
                var symbols = latest?.Models?.Values.Distinct().SelectMany(model => model.WorkspaceSymbols(search))
                    .DistinctBy(symbol => (symbol.Name, symbol.Location.Uri, symbol.Location.Range))
                    .OrderBy(symbol => symbol.Name, StringComparer.Ordinal).ThenBy(symbol => symbol.Location.Uri, StringComparer.Ordinal)
                    .ThenBy(symbol => symbol.Location.Range.Start.Line).ThenBy(symbol => symbol.Location.Range.Start.Character).ToArray() ?? [];
                await Reply(query.Id, symbols);
                return;
            }
            if (query.Method is "typeHierarchy/supertypes" or "typeHierarchy/subtypes")
            {
                var data = query.Parameters.GetProperty("item").GetProperty("data");
                string hierarchyPath = data.GetProperty("path").GetString()!;
                int hierarchyOffset = data.GetProperty("offset").GetInt32();
                await Reply(query.Id, latest?.Models?.GetValueOrDefault(hierarchyPath)?.TypeHierarchy(hierarchyPath, hierarchyOffset,
                    query.Method == "typeHierarchy/subtypes") ?? []);
                return;
            }
            string path = Workspace.FilePath(query.Parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
            if (!documents.TryGetValue(path, out var document))
            { await Reply(query.Id, null); return; }
            int offset = query.Parameters.TryGetProperty("position", out var position) ? Workspace.Offset(document.Text, position) : 0;
            if (IsFormattingRequest(query.Method))
            {
                var settings = query.Parameters.GetProperty("options");
                var format = new FormattingOptions(settings.GetProperty("tabSize").GetInt32(), settings.GetProperty("insertSpaces").GetBoolean(),
                    settings.TryGetProperty("gflat.bracesOnNewLine", out var placement) ? placement.GetBoolean() : bracesOnNewLine);
                TextRange? selectedRange = query.Parameters.TryGetProperty("range", out var selected) ? JsonSerializer.Deserialize<TextRange>(selected, Protocol.JsonOptions) : null;
                if (query.Method == "textDocument/onTypeFormatting")
                {
                    int line = position.GetProperty("line").GetInt32();
                    selectedRange = new(new(line, 0), new(line, int.MaxValue));
                }
                await Reply(query.Id, SourceFormatter.Format(new SourceFile(path, document.Text), format, selectedRange));
                return;
            }
            if (latest?.Models?.GetValueOrDefault(path) is not EditorModel model)
            { await Reply(query.Id, null); return; }
            TextRange? range = query.Parameters.TryGetProperty("range", out var element)
                ? JsonSerializer.Deserialize<TextRange>(element, Protocol.JsonOptions) : null;
            object? result = query.Method switch
            {
                "textDocument/completion" => model.Completion(path, offset),
                "textDocument/hover" => model.Hover(path, offset, hoverPresentation, hoverOptions),
                "textDocument/definition" => model.Definition(path, offset),
                "textDocument/implementation" => model.Implementations(path, offset),
                "textDocument/codeAction" => model.CodeActions(path, range ?? new(new(0, 0), new(0, 0))).Where(action =>
                    !query.Parameters.TryGetProperty("context", out var context) || !context.TryGetProperty("only", out var only) ||
                    only.EnumerateArray().Any(kind => action.Kind == kind.GetString() || action.Kind.StartsWith(kind.GetString() + ".", StringComparison.Ordinal))).ToArray(),
                "textDocument/prepareTypeHierarchy" => model.PrepareTypeHierarchy(path, offset),
                "textDocument/documentHighlight" => model.DocumentHighlights(path, offset),
                "textDocument/prepareRename" => model.PrepareRename(path, offset),
                "textDocument/rename" => RenameWorkspaceEdit(model, path, offset,
                    query.Parameters.GetProperty("newName").GetString() ?? throw new ArgumentException("newName must be a string")),
                "textDocument/references" => model.References(path, offset,
                    query.Parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean()),
                "textDocument/documentSymbol" => model.DocumentSymbols(path, hierarchicalDocumentSymbols),
                "textDocument/signatureHelp" => model.SignatureHelp(path, offset, tokenTypes == EditorModel.VisualStudioTokenTypes),
                _ => model.SemanticTokens(path, range, tokenTypes == EditorModel.VisualStudioTokenTypes)
            };
            await Reply(query.Id, result);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException or IOException or UnauthorizedAccessException)
        { await Error(query.Id, -32602, error.Message); }
    }

    private object RenameWorkspaceEdit(EditorModel model, string path, int offset, string name)
    {
        var groups = model.Rename(path, offset, name).GroupBy(edit => edit.Path, Workspace.Paths).ToArray();
        if (!versionedWorkspaceEdits)
            return new { changes = groups.ToDictionary(group => Workspace.FileUri(group.Key),
                group => group.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray()) };
        return new { documentChanges = groups.Select(group => new {
            textDocument = new Dictionary<string, object?> { ["uri"] = Workspace.FileUri(group.Key), ["version"] = documents.GetValueOrDefault(group.Key)?.Version },
            edits = group.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray()
        }).ToArray() };
    }

    private static int QueryPriority(string method) => method switch
    {
        "textDocument/completion" or "textDocument/signatureHelp" => 0,
        "textDocument/hover" or "textDocument/definition" => 1,
        _ => 2
    };

    private bool IgnoreGeneratedChange(string path) => !watchedPaths.Contains(path) &&
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(gflat.Projects.ProjectModel.IsGeneratedDirectory);

    private bool RelevantFileChange(string path)
    {
        if (IgnoreGeneratedChange(path)) return false;
        if (latest == null || watchedPaths.Contains(path) ||
            ProjectWorkspace.IsProjectDiscoveryChange(path, projectDiscoveryDirectories) ||
            root != null && Workspace.Paths.Equals(path, Path.Combine(root, Workspace.ConfigurationName))) return true;
        // Use the completed graph's immutable glob rules, rather than querying
        // the project cache while the next analysis may be updating it.
        if (latest.SourceProjects?.Any(project => project.IncludesFile(path)) == true) return true;
        string prefix = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return watchedPaths.Any(input => input.StartsWith(prefix, comparison)) ||
            Directory.Exists(path) && projectDirectories.Any(directory => path.StartsWith(directory + Path.DirectorySeparatorChar, comparison));
    }

    private void UpdateFormattingOptions(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return;
        if (settings.TryGetProperty("gflat", out var nested)) settings = nested;
        if (settings.TryGetProperty("bracesOnNewLine", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            bracesOnNewLine = value.GetBoolean();
    }

    private static bool IsFormattingRequest(string method)
        => method is "textDocument/formatting" or "textDocument/rangeFormatting" or "textDocument/onTypeFormatting";

    private static bool RequiresFullAnalysis(string method)
        => method is "textDocument/references" or "textDocument/rename" or "textDocument/prepareRename" or
            "textDocument/implementation" or "textDocument/prepareTypeHierarchy" or "typeHierarchy/supertypes" or "typeHierarchy/subtypes" or "textDocument/codeAction" or "workspace/executeCommand";

    private async Task Schedule(bool invalidateProjects = false)
    {
        if (invalidateProjects) projectRevision++;
        long requestedProjectRevision = projectRevision;
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        CancellationToken cancellation = pending.Token;
        long revision = ++generation;
        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fullRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        interactiveRequested = requested;
        fullAnalysisRequested = fullRequested;
        if (invalidateProjects)
        {
            // External inputs changed, but the open documents and request
            // positions did not. Answer against the refreshed graph.
            for (int i = 0; i < editorRequests.Count; i++)
                editorRequests[i] = editorRequests[i] with { Generation = revision };
            if (editorRequests.Count > 0) requested.TrySetResult(true);
            if (editorRequests.Any(query => RequiresFullAnalysis(query.Method))) fullRequested.TrySetResult(true);
        }
        else
        {
            foreach (var query in editorRequests) await Error(query.Id, -32801, "Document changed");
            editorRequests.Clear();
        }
        string? workspaceRoot = root;
        var snapshot = new Dictionary<string, OpenDocument>(documents, Workspace.Paths);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAny(Task.Delay(150, cancellation), requested.Task);
                await analysisGate.WaitAsync(cancellation);
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (requestedProjectRevision != evaluatedProjectRevision)
                    { projects.Invalidate(); evaluatedProjectRevision = requestedProjectRevision; }
                    if (snapshot.Count > 0)
                    {
                        var focused = Workspace.Analyze(workspaceRoot, snapshot, projects, cancellation,
                            new HashSet<string>(snapshot.Keys, Workspace.Paths));
                        if (!cancellation.IsCancellationRequested) events.Writer.TryWrite(new Finished(revision, focused, snapshot, requestedProjectRevision));
                        if (!focused.IsPartial) return;
                        // Requests needing unopened bodies bypass the idle timer.
                        // Completion and hover use the focused result without forcing a full check.
                        await Task.WhenAny(Task.Delay(500, cancellation), fullRequested.Task);
                        cancellation.ThrowIfCancellationRequested();
                    }
                    var result = Workspace.Analyze(workspaceRoot, snapshot, projects, cancellation);
                    if (!cancellation.IsCancellationRequested) events.Writer.TryWrite(new Finished(revision, result, snapshot, requestedProjectRevision));
                }
                finally { analysisGate.Release(); }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                await log.WriteLineAsync(error.ToString());
            }
        });
    }

    private void UpdateWatchers()
    {
        var directories = watchedPaths.Select(Path.GetDirectoryName).OfType<string>()
            .Concat(projectDirectories).Concat(projectDiscoveryDirectories).Distinct(Workspace.Paths)
            .Where(Directory.Exists).ToDictionary(directory => directory, projectDirectories.Contains, Workspace.Paths);
        if (!watchersNeedRefresh && watcherInputs.SetEquals(watchedPaths) && directories.Count == watcherDirectoryInputs.Count &&
            directories.All(pair => watcherDirectoryInputs.TryGetValue(pair.Key, out bool recursive) && recursive == pair.Value)) return;
        watchersNeedRefresh = false;
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
        var explicitPaths = new HashSet<string>(watchedPaths, Workspace.Paths);
        watcherInputs = explicitPaths;
        watcherDirectoryInputs = directories;
        // Watch only source/config directories, never recursively scan or watch build output trees.
        foreach (var entry in directories)
        {
            string directory = entry.Key;
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = entry.Value, NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                void ChangedPath(string path)
                {
                    if (explicitPaths.Contains(path) || !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(gflat.Projects.ProjectModel.IsGeneratedDirectory))
                        events.Writer.TryWrite(new Changed(path));
                }
                watcher.Changed += (_, e) => ChangedPath(e.FullPath);
                watcher.Created += (_, e) => ChangedPath(e.FullPath);
                watcher.Deleted += (_, e) => ChangedPath(e.FullPath);
                watcher.Renamed += (_, e) => { ChangedPath(e.OldFullPath); ChangedPath(e.FullPath); };
                watcher.Error += (_, _) =>
                {
                    watchersNeedRefresh = true;
                    events.Writer.TryWrite(new Changed(directory, Force: true));
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                watcher?.Dispose();
                watchersNeedRefresh = true;
                log.WriteLine(error.Message);
            }
        }
    }
}
