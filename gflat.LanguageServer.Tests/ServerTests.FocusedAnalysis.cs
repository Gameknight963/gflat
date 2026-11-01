using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task ReferencesAfterCompletionIncludeBoundCallsInUnopenedBodies()
    {
        await using var session = new Session();
        File.WriteAllText(session.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        const string main = "int Answer(int value) => value; int Answer(bool value) => 0; int main() => Answer(1);";
        const string helper = "int FromDisk() => Answer(2); int OtherOverload() => Answer(true);";
        File.WriteAllText(session.File("helper.gf"), helper);
        await session.Initialize();
        await session.Open("main.gf", main);
        await session.Send(new { jsonrpc = "2.0", id = 810, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("main.gf") },
            position = new { line = 0, character = main.LastIndexOf("Answer", StringComparison.Ordinal) + 3 }
        } });
        await session.Send(new { jsonrpc = "2.0", id = 811, method = "textDocument/references", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = main.IndexOf("Answer", StringComparison.Ordinal) },
            context = new { includeDeclaration = true }
        } });
        var completion = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 810);
        Assert.True(completion.TryGetProperty("result", out _));
        var references = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 811);
        var locations = references.GetProperty("result");
        Assert.Equal(3, locations.GetArrayLength());
        var diskReference = Assert.Single(locations.EnumerateArray(), location => location.GetProperty("uri").GetString() == session.Uri("helper.gf"));
        Assert.Equal(helper.IndexOf("Answer", StringComparison.Ordinal),
            diskReference.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        await session.Stop();
    }

    [Theory]
    [InlineData("textDocument/prepareRename")]
    [InlineData("textDocument/rename")]
    public async Task RenameAfterCompletionChecksErrorsInUnopenedBodies(string method)
    {
        await using var session = new Session();
        File.WriteAllText(session.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        File.WriteAllText(session.File("helper.gf"), "int Broken() => missingUnopenedFunction();");
        const string main = "int Identity(int value) => value;";
        await session.Initialize();
        await session.Open("main.gf", main);
        await session.Send(new { jsonrpc = "2.0", id = 812, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("main.gf") },
            position = new { line = 0, character = main.LastIndexOf("value", StringComparison.Ordinal) + 3 }
        } });
        await session.Send(new { jsonrpc = "2.0", id = 813, method, @params = new {
            textDocument = new { uri = session.Uri("main.gf") },
            position = new { line = 0, character = main.IndexOf("value", StringComparison.Ordinal) }, newName = "renamed"
        } });
        var completion = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 812);
        Assert.True(completion.TryGetProperty("result", out _));
        var response = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 813);
        Assert.Equal(-32602, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("error-free project", response.GetProperty("error").GetProperty("message").GetString());
        await session.Stop();
    }
}
