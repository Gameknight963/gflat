using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task HeaderContextCommandChangesCompletion()
    {
        await using var session = new Session();
        const string header = "public int Read() { return Value; }";
        File.WriteAllText(session.File("members.gfh"), header);
        await session.Initialize();
        await session.Open("main.gf", "struct First { public int Value; public int FirstOnly;\n#include \"members.gfh\"\n}\nstruct Second { public int Value; public int SecondOnly;\n#include \"members.gfh\"\n}");
        await session.Open("members.gfh", header);
        await session.Send(new { jsonrpc = "2.0", id = 91, method = "textDocument/codeAction", @params = new {
            textDocument = new { uri = session.Uri("members.gfh") }, range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } }, context = new { diagnostics = Array.Empty<object>() } } });
        var actions = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "91");
        var command = Assert.Single(actions.GetProperty("result").EnumerateArray(), a => a.GetProperty("title").GetString()!.EndsWith(": Second")).GetProperty("command");
        await session.Send(new { jsonrpc = "2.0", id = 92, method = "workspace/executeCommand", @params = command });
        var executed = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "92");
        Assert.False(executed.TryGetProperty("error", out _));
        await session.Send(new { jsonrpc = "2.0", id = 93, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("members.gfh") }, position = new { line = 0, character = header.IndexOf("Value", StringComparison.Ordinal) } } });
        var completion = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "93");
        var labels = completion.GetProperty("result").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToArray();
        Assert.Contains("SecondOnly", labels);
        Assert.DoesNotContain("FirstOnly", labels);
        await session.Stop();
    }
}
