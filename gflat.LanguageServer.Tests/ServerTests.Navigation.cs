namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task ImplementationAndHierarchyRequestsRunThroughTheServer()
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("main.gf", "interface I { int Read(); } class C : I { public int Read() => 1; }");
        await session.Send(new { jsonrpc = "2.0", id = 71, method = "textDocument/implementation",
            @params = new { textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = 18 } } });
        var implemented = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "71");
        Assert.Single(implemented.GetProperty("result").EnumerateArray());
        await session.Send(new { jsonrpc = "2.0", id = 72, method = "textDocument/prepareTypeHierarchy",
            @params = new { textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = 10 } } });
        var prepared = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "72");
        var item = Assert.Single(prepared.GetProperty("result").EnumerateArray());
        await session.Send(new { jsonrpc = "2.0", id = 73, method = "typeHierarchy/subtypes", @params = new { item } });
        var subtypes = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "73");
        Assert.Equal("C", Assert.Single(subtypes.GetProperty("result").EnumerateArray()).GetProperty("name").GetString());
        await session.Stop();
    }
}
