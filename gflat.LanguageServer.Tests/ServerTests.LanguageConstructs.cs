using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task LanguageConstructExplanationsCanBeEnabledAndDisabledWithoutEditing()
    {
        await using var session = new Session();
        await session.Initialize();
        const string source = "#define ANSWER 42\nvoid Run() { if (true) return; }";
        await session.Open("learning.gf", source);
        await session.Diagnostics("learning.gf", 1);
        int id = 9700;
        async Task<JsonElement> Hover(int line, int character)
        {
            int request = id++;
            await session.Send(new { jsonrpc = "2.0", id = request, method = "textDocument/hover", @params = new {
                textDocument = new { uri = session.Uri("learning.gf") }, position = new { line, character } } });
            var response = await session.Receive(x => x.TryGetProperty("id", out var n) && n.ValueKind == JsonValueKind.Number && n.GetInt32() == request);
            return response.GetProperty("result");
        }
        Assert.Equal(JsonValueKind.Null, (await Hover(0, 2)).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Hover(1, 14)).ValueKind);
        await session.Notify("workspace/didChangeConfiguration", new { settings = new { gflat = new { showLanguageConstructs = true } } });
        Assert.Contains("macro", (await Hover(0, 2)).GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("condition is true", (await Hover(1, 14)).GetProperty("contents").GetProperty("value").GetString());
        await session.Notify("workspace/didChangeConfiguration", new { settings = new { gflat = new { showLanguageConstructs = false } } });
        Assert.Equal(JsonValueKind.Null, (await Hover(0, 2)).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Hover(1, 14)).ValueKind);
        await session.Stop();
    }
}
