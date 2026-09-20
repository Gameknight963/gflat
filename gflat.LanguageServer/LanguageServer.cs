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
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly SemaphoreSlim analysisGate = new(1, 1);
    private CancellationTokenSource? pending;
    private long generation;
    private string? root;
    private bool initialized, shutdown;

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
                        if (watchedPaths.Contains(changed.Path) || root != null && Workspace.Paths.Equals(changed.Path, Path.Combine(root, Workspace.ConfigurationName))) Schedule();
                        break;
                    case Finished result when result.Generation == generation && !shutdown:
                        watchedPaths.Clear();
                        watchedPaths.UnionWith(result.Result.WatchedPaths);
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
                        await Reply(id, new { capabilities = new { positionEncoding = "utf-16", textDocumentSync = new { openClose = true, change = 1, save = new { includeText = true } } }, serverInfo = new { name = "gflat", version = "0.1.0" } });
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
                    case "workspace/didChangeConfiguration": Schedule(); break;
                    case "$/cancelRequest":
                    case "$/setTrace": break;
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

    private void Schedule()
    {
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
                    var result = Workspace.Analyze(workspaceRoot, snapshot);
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
        // Watch only source/config directories, never recursively scan or watch build output trees.
        foreach (string directory in watchedPaths.Select(Path.GetDirectoryName).OfType<string>().Distinct(Workspace.Paths))
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Changed += (_, e) => events.Writer.TryWrite(new Changed(e.FullPath));
                watcher.Created += (_, e) => events.Writer.TryWrite(new Changed(e.FullPath));
                watcher.Deleted += (_, e) => events.Writer.TryWrite(new Changed(e.FullPath));
                watcher.Renamed += (_, e) => { events.Writer.TryWrite(new Changed(e.OldFullPath)); events.Writer.TryWrite(new Changed(e.FullPath)); };
                watcher.Error += (_, _) => events.Writer.TryWrite(new Changed(Path.Combine(root!, Workspace.ConfigurationName)));
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (IOException error) { log.WriteLine(error.Message); }
        }
    }
}
