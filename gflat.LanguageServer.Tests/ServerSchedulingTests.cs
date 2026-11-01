using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task CompletionRepliesPrecedeDiagnosticsAndUnchangedSaveDoesNotRepublishThem()
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("main.gf", "int Answer() => 42;");
        await session.Diagnostics("main.gf", 1);
        const string changed = "int Answer() { return 43; }";
        await session.Change("main.gf", changed, 2);
        await session.Send(new { jsonrpc = "2.0", id = 1401, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = 15 } } });
        int beforeResponse = 0;
        var completion = await session.Receive(message => {
            if (message.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics") beforeResponse++;
            return message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1401;
        });
        Assert.False(completion.TryGetProperty("error", out _));
        Assert.Equal(0, beforeResponse);
        await session.Diagnostics("main.gf", 2);
        await session.Notify("textDocument/didSave", new { textDocument = new { uri = session.Uri("main.gf") }, text = changed });
        await session.Send(new { jsonrpc = "2.0", id = 1402, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = 15 } } });
        beforeResponse = 0;
        await session.Receive(message => {
            if (message.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics") beforeResponse++;
            return message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1402;
        });
        Assert.Equal(0, beforeResponse);
        await session.Send(new { jsonrpc = "2.0", id = 1403, method = "workspace/symbol", @params = new { query = "Answer" } });
        await session.Receive(message => {
            if (message.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics") beforeResponse++;
            return message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1403;
        });
        Assert.Equal(0, beforeResponse);
        await session.Stop();
    }

    [Fact]
    public async Task DiagnosticsForUnopenedFilesArePublishedAndClearedWhenFixed()
    {
        await using var session = new Session();
        System.IO.File.WriteAllText(session.File(Workspace.ConfigurationName), """{"sources":["main.gf","other.gf"]}""");
        System.IO.File.WriteAllText(session.File("main.gf"), "int main() => Answer();");
        System.IO.File.WriteAllText(session.File("other.gf"), "int Answer() => missing;");
        await session.Initialize();
        await session.Open("main.gf", "int main() => Answer();");
        var error = await session.Diagnostics("other.gf");
        Assert.Contains(error.GetProperty("params").GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("message").GetString()!.Contains("missing"));
        System.IO.File.WriteAllText(session.File("other.gf"), "int Answer() => 42;");
        await session.Notify("workspace/didChangeWatchedFiles", new { changes = new[] { new { uri = session.Uri("other.gf"), type = 2 } } });
        var cleared = await session.Diagnostics("other.gf");
        Assert.Empty(cleared.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
    }
}
