namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task RenameUsesNullVersionForUnopenedSourceFiles()
    {
        await using var session = new Session();
        File.WriteAllText(session.File("gflat-workspace.json"), "{\"sources\":[\"main.gf\",\"library.gf\"]}");
        File.WriteAllText(session.File("library.gf"), "public int Read() => 42;");
        await session.Initialize(clientCapabilities: new { workspace = new { workspaceEdit = new { documentChanges = true } } });
        await session.Open("main.gf", "int F() => Read();");
        await session.Send(new { jsonrpc = "2.0", id = 95, method = "textDocument/rename", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = 11 }, newName = "Renamed" } });
        var response = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "95");
        var changes = response.GetProperty("result").GetProperty("documentChanges").EnumerateArray().ToArray();
        var library = Assert.Single(changes, c => c.GetProperty("textDocument").GetProperty("uri").GetString() == session.Uri("library.gf"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, library.GetProperty("textDocument").GetProperty("version").ValueKind);
        await session.Stop();
    }
}
