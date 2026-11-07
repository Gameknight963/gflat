using System.Text.Json;
using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task AReadOnlyDirectoryDoesNotProtectASiblingWithTheSamePrefix()
    {
        await using var session = new Session();
        Directory.CreateDirectory(session.File("library-other"));
        await session.Initialize(new { readOnlySourceRoots = new[] { session.File("library") } });
        const string name = "library-other/types.gf";
        await session.Open(name, "struct Item{}");
        await session.Diagnostics(name, 1);
        await session.Send(new { jsonrpc = "2.0", id = 81, method = "textDocument/rename", @params = new {
            textDocument = new { uri = session.Uri(name) }, position = new { line = 0, character = 7 }, newName = "Renamed" } });
        var response = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ToString() == "81");
        Assert.True(response.GetProperty("result").GetProperty("changes").TryGetProperty(session.Uri(name), out _));
        await session.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameDoesNotReturnPartialEditsWhenAReferenceOrContractIsReadOnly(bool contract)
    {
        await using var session = new Session();
        Directory.CreateDirectory(session.File("library"));
        File.WriteAllText(session.File("app.gfproj"), ProjectFileFixture.Xml());
        string main = contract ? "struct Item:I{public int Read()=>1;}" : "public struct Item{} int F()=>0;";
        string library = contract ? "interface I{int Read();}" : "int Use(Item item)=>0;";
        File.WriteAllText(session.File("main.gf"), main);
        File.WriteAllText(session.File("library/types.gf"), library);
        await session.Initialize(new { readOnlySourceRoots = new[] { session.File("library") } });
        await session.Open("main.gf", main);
        var diagnostics = (await session.Diagnostics("main.gf", 1)).GetProperty("params").GetProperty("diagnostics");
        Assert.DoesNotContain(diagnostics.EnumerateArray(), diagnostic => diagnostic.GetProperty("severity").GetInt32() == 1);
        await session.Send(new { jsonrpc = "2.0", id = 81, method = "textDocument/rename", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = main.IndexOf(contract ? "Read" : "Item", StringComparison.Ordinal) }, newName = "Renamed" } });
        var response = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ToString() == "81");
        Assert.Contains("read-only", response.GetProperty("error").GetProperty("message").GetString());
        Assert.False(response.TryGetProperty("result", out _));
        await session.Stop();
    }

    [Theory]
    [InlineData("prepareRename")]
    [InlineData("rename")]
    [InlineData("formatting")]
    [InlineData("codeAction")]
    public async Task ReadOnlyLibrarySourcesDoNotProduceEditingOperations(string operation)
    {
        await using var session = new Session();
        string library = session.File("library");
        Directory.CreateDirectory(library);
        const string name = "library/types.gf";
        await session.Initialize(new { readOnlySourceRoots = new[] { library } });
        await session.Open(name, "struct Item{private int value;}");
        await session.Diagnostics(name, 1);
        object parameters = operation switch {
            "formatting" => new { textDocument = new { uri = session.Uri(name) }, options = new { tabSize = 4, insertSpaces = true } },
            "codeAction" => new { textDocument = new { uri = session.Uri(name) }, range = new { start = new { line = 0, character = 7 }, end = new { line = 0, character = 7 } }, context = new { diagnostics = Array.Empty<object>() } },
            "rename" => new { textDocument = new { uri = session.Uri(name) }, position = new { line = 0, character = 7 }, newName = "Renamed" },
            _ => new { textDocument = new { uri = session.Uri(name) }, position = new { line = 0, character = 7 } }
        };
        await session.Send(new { jsonrpc = "2.0", id = 81, method = "textDocument/" + operation, @params = parameters });
        var response = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ToString() == "81");
        if (operation == "rename") Assert.Contains("read-only", response.GetProperty("error").GetProperty("message").GetString());
        else if (operation == "prepareRename") Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
        else Assert.Empty(response.GetProperty("result").EnumerateArray());
        await session.Send(new { jsonrpc = "2.0", id = 82, method = "textDocument/definition", @params = new {
            textDocument = new { uri = session.Uri(name) }, position = new { line = 0, character = 7 } } });
        var definition = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ToString() == "82");
        Assert.Equal(session.Uri(name), definition.GetProperty("result").GetProperty("uri").GetString());
        await session.Stop();
    }
}
