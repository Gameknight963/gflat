using System.Text.Json;
using System.Threading.Channels;

namespace gflat.LanguageServer;

/// <summary>One event loop owns documents and stdout. Analysis runs serially in the background.</summary>
public sealed class LanguageServer(Stream input, Stream output, TextWriter log)
{
    private abstract record Event;
    private sealed record Message(byte[] Body) : Event;
    private sealed record Finished(long Generation, Analysis Result) : Event;
    private sealed record Changed(string Path) : Event;
    private sealed record InputEnded(Exception? Error) : Event;
    private readonly Channel<Event> events = Channel.CreateUnbounded<Event>(new() { SingleReader = true });
    private readonly Dictionary<string, OpenDocument> documents = new(Workspace.Paths);
    private readonly HashSet<string> published = new(Workspace.Paths);
    private readonly HashSet<string> watchedPaths = new(Workspace.Paths);
    private readonly HashSet<string> projectDirectories = new(Workspace.Paths);
    private readonly HashSet<string> projectDiscoveryDirectories = new(Workspace.Paths);
    private readonly ProjectWorkspace projects = new();
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly SemaphoreSlim analysisGate = new(1, 1);
    private CancellationTokenSource? pending;
    private long generation;
    private long projectRevision, evaluatedProjectRevision = -1;
    private string? root;
    private bool initialized, shutdown;
    private Analysis? latest;
    private long analyzedGeneration = -1;
    private sealed record EditorRequest(JsonElement Id, string Method, JsonElement Parameters, long Generation);
    private readonly List<EditorRequest> editorRequests = [];
    private string[] tokenTypes = EditorModel.TokenTypes;
    private VisualStudioHover? hoverPresentation;
    private HoverOptions hoverOptions = new();
    private bool versionedWorkspaceEdits;
    private bool hierarchicalDocumentSymbols;

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
                        if (watchedPaths.Contains(changed.Path) ||
                            ProjectWorkspace.IsProjectDiscoveryChange(changed.Path, projectDiscoveryDirectories) ||
                            projectDirectories.Any(dir => changed.Path.StartsWith(dir + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) ||
                            root != null && Workspace.Paths.Equals(changed.Path, Path.Combine(root, Workspace.ConfigurationName)))
                        { Schedule(invalidateProjects: true); }
                        break;
                    case Finished result when result.Generation == generation && !shutdown:
                        latest = result.Result;
                        analyzedGeneration = result.Generation;
                        watchedPaths.Clear();
                        watchedPaths.UnionWith(result.Result.WatchedPaths);
                        projectDirectories.Clear();
                        projectDirectories.UnionWith(result.Result.ProjectDirectories ?? []);
                        projectDiscoveryDirectories.Clear();
                        projectDiscoveryDirectories.UnionWith(result.Result.ProjectDiscoveryDirectories ?? []);
                        UpdateWatchers();
                        foreach (string path in published.Union(result.Result.Diagnostics.Keys, Workspace.Paths).ToArray())
                        {
                            documents.TryGetValue(path, out var document);
                            await Send(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new
                            {
                                uri = Workspace.FileUri(path), version = document?.Version,
                                diagnostics = result.Result.Diagnostics.GetValueOrDefault(path) ?? []
                            } });
                        }
                        published.Clear();
                        published.UnionWith(result.Result.Diagnostics.Keys);
                        foreach (var query in editorRequests.ToArray()) await AnswerEditor(query);
                        editorRequests.Clear();
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
                        if (parameters.TryGetProperty("initializationOptions", out var options) && options.ValueKind == JsonValueKind.Object &&
                            options.TryGetProperty("visualStudioClassifications", out var vsColors) && vsColors.ValueKind == JsonValueKind.True)
                            tokenTypes = EditorModel.VisualStudioTokenTypes;
                        if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("hoverIcons", out var hoverIcons))
                            hoverPresentation = new VisualStudioHover(hoverIcons);
                        hoverOptions = hoverOptions.Update(options);
                        await Reply(id, new { capabilities = new {
                            positionEncoding = "utf-16",
                            textDocumentSync = new { openClose = true, change = 1, save = new { includeText = true } },
                            completionProvider = new { triggerCharacters = new[] { ".", ":" } },
                            hoverProvider = !(options.ValueKind == JsonValueKind.Object && options.TryGetProperty("visualStudioNativeHover", out var nativeHover) && nativeHover.ValueKind == JsonValueKind.True), definitionProvider = true, referencesProvider = true, documentHighlightProvider = true,
                            renameProvider = new { prepareProvider = true }, workspaceSymbolProvider = true,
                            signatureHelpProvider = new { triggerCharacters = new[] { "(", "," }, retriggerCharacters = new[] { ")" } }, documentSymbolProvider = true,
                            semanticTokensProvider = new { legend = new { tokenTypes, tokenModifiers = EditorModel.TokenModifiers }, full = true, range = true }
                        }, serverInfo = new { name = "gflat", version = "0.2.0" } });
                        break;
                    case "workspace/didChangeConfiguration":
                        if (parameters.TryGetProperty("settings", out var settings)) hoverOptions = hoverOptions.Update(settings);
                        break;
                    case "initialized": Schedule(); break;
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
                        Schedule();
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
                        Schedule();
                        break;
                    }
                    case "textDocument/didSave":
                    {
                        string path = Workspace.FilePath(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
                        if (documents.TryGetValue(path, out var previous) && parameters.TryGetProperty("text", out var text))
                            documents[path] = previous with { Text = text.GetString()! };
                        Schedule();
                        break;
                    }
                    case "textDocument/didClose":
                        documents.Remove(Workspace.FilePath(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!));
                        Schedule();
                        break;
                    case "workspace/didChangeWatchedFiles":
                        Schedule(invalidateProjects: true); break;
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
                            if (analyzedGeneration == generation) await AnswerEditor(query);
                            else editorRequests.Add(query);
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
            string path = Workspace.FilePath(query.Parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
            if (latest?.Models?.GetValueOrDefault(path) is not EditorModel model || !documents.TryGetValue(path, out var document))
            { await Reply(query.Id, null); return; }
            int offset = query.Parameters.TryGetProperty("position", out var position) ? Workspace.Offset(document.Text, position) : 0;
            TextRange? range = query.Parameters.TryGetProperty("range", out var element)
                ? JsonSerializer.Deserialize<TextRange>(element, Protocol.JsonOptions) : null;
            object? result = query.Method switch
            {
                "textDocument/completion" => model.Completion(path, offset),
                "textDocument/hover" => model.Hover(path, offset, hoverPresentation, hoverOptions),
                "textDocument/definition" => model.Definition(path, offset),
                "textDocument/documentHighlight" => model.DocumentHighlights(path, offset),
                "textDocument/prepareRename" => model.PrepareRename(path, offset),
                "textDocument/rename" => RenameWorkspaceEdit(model, path, offset,
                    query.Parameters.GetProperty("newName").GetString() ?? throw new ArgumentException("newName must be a string")),
                "textDocument/references" => model.References(path, offset,
                    query.Parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean()),
                "textDocument/documentSymbol" => model.DocumentSymbols(path, hierarchicalDocumentSymbols),
                "textDocument/signatureHelp" => model.SignatureHelp(path, offset, tokenTypes == EditorModel.VisualStudioTokenTypes),
                _ => model.SemanticTokens(path, range)
            };
            await Reply(query.Id, result);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException)
        { await Error(query.Id, -32602, error.Message); }
    }

    private object RenameWorkspaceEdit(EditorModel model, string path, int offset, string name)
    {
        var groups = model.Rename(path, offset, name).GroupBy(edit => edit.Path, Workspace.Paths).ToArray();
        if (!versionedWorkspaceEdits)
            return new { changes = groups.ToDictionary(group => Workspace.FileUri(group.Key),
                group => group.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray()) };
        return new { documentChanges = groups.Select(group => new {
            textDocument = new { uri = Workspace.FileUri(group.Key), version = documents[group.Key].Version },
            edits = group.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray()
        }).ToArray() };
    }

    private void Schedule(bool invalidateProjects = false)
    {
        if (invalidateProjects) projectRevision++;
        long requestedProjectRevision = projectRevision;
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        CancellationToken cancellation = pending.Token;
        long revision = ++generation;
        string? workspaceRoot = root;
        var snapshot = new Dictionary<string, OpenDocument>(documents, Workspace.Paths);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, cancellation);
                await analysisGate.WaitAsync(cancellation);
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (requestedProjectRevision != evaluatedProjectRevision)
                    { projects.Invalidate(); evaluatedProjectRevision = requestedProjectRevision; }
                    var result = Workspace.Analyze(workspaceRoot, snapshot, projects);
                    if (!cancellation.IsCancellationRequested) events.Writer.TryWrite(new Finished(revision, result));
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
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
        var explicitPaths = new HashSet<string>(watchedPaths, Workspace.Paths);
        // Watch only source/config directories, never recursively scan or watch build output trees.
        foreach (string directory in watchedPaths.Select(Path.GetDirectoryName).OfType<string>().Concat(projectDirectories).Concat(projectDiscoveryDirectories).Distinct(Workspace.Paths))
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                var watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = projectDirectories.Contains(directory), NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                void ChangedPath(string path)
                {
                    if (explicitPaths.Contains(path) || !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(gflat.Projects.ProjectModel.IsGeneratedDirectory))
                        events.Writer.TryWrite(new Changed(path));
                }
                watcher.Changed += (_, e) => ChangedPath(e.FullPath);
                watcher.Created += (_, e) => ChangedPath(e.FullPath);
                watcher.Deleted += (_, e) => ChangedPath(e.FullPath);
                watcher.Renamed += (_, e) => { ChangedPath(e.OldFullPath); ChangedPath(e.FullPath); };
                watcher.Error += (_, _) => events.Writer.TryWrite(new Changed(Path.Combine(root!, Workspace.ConfigurationName)));
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (IOException error) { log.WriteLine(error.Message); }
        }
    }
}
