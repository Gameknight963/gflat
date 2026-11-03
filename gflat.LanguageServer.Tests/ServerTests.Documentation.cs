using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task DocumentationWarningsAndManualExportAreAvailableThroughTheServer()
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("main.gf", "/// @brief Reads a value.\n/// @param missing Wrong parameter\nint Read(int value) => value;");
        var diagnostics = await session.Diagnostics("main.gf");
        Assert.Contains(diagnostics.GetProperty("params").GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "GFLS0003" && d.GetProperty("severity").GetInt32() == 2);
        await session.Send(new { jsonrpc = "2.0", id = 94, method = "workspace/executeCommand", @params = new {
            command = "gflat.generateDocumentation", arguments = new[] { session.Uri("main.gf"), session.File("manual") } } });
        var generated = await session.Receive(x => x.TryGetProperty("id", out var id) && id.ToString() == "94");
        Assert.False(generated.TryGetProperty("error", out _));
        Assert.Contains("Reads a value.", File.ReadAllText(session.File("manual/index.html")));
        using var json = JsonDocument.Parse(File.ReadAllText(session.File("manual/documentation.json")));
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
        Assert.Single(json.RootElement.GetProperty("entries").EnumerateArray());
        await session.Stop();
    }
}
