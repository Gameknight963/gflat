namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task FormattingRequestsUseCurrentDocumentAndConfiguration()
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("main.gf", "int F(){return 1;}");
        await session.Notify("workspace/didChangeConfiguration", new { settings = new { gflat = new { bracesOnNewLine = false } } });
        await session.Send(new { jsonrpc = "2.0", id = 81, method = "textDocument/formatting",
            @params = new { textDocument = new { uri = session.Uri("main.gf") }, options = new { tabSize = 2, insertSpaces = true } } });
        var response = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "81");
        var edits = response.GetProperty("result").EnumerateArray().ToArray();
        Assert.Contains(edits, edit => edit.GetProperty("newText").GetString() == "\n  ");
        Assert.Contains(edits, edit => edit.GetProperty("newText").GetString() == " ");
        await session.Stop();
    }
}
