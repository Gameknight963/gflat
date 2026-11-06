using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task CustomSourceGlobsStillObserveNewFilesAndTheirDiagnostics()
    {
        await using var session = new Session();
        System.IO.File.WriteAllText(session.File("app.gfproj"), gflat.TestSupport.ProjectFileFixture.WithImports("""
            <Project>
              <PropertyGroup><NoStdLib>true</NoStdLib><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
              <ItemGroup><Compile Include="main.gf" /><Compile Include="*.custom" /></ItemGroup>
            </Project>
            """));
        await session.Initialize();
        await session.Open("main.gf", "int main() => 0;");
        await session.Diagnostics("main.gf", 1);
        System.IO.File.WriteAllText(session.File("new.custom"), "int Read() => missing;");
        await session.Notify("workspace/didChangeWatchedFiles", new { changes = new[] {
            new { uri = session.Uri("new.custom"), type = 1 } } });
        var error = await session.Diagnostics("new.custom");
        Assert.Contains("missing", error.ToString());
        System.IO.File.WriteAllText(session.File("new.custom"), "int Read() => 42;");
        await session.Notify("workspace/didChangeWatchedFiles", new { changes = new[] {
            new { uri = session.Uri("new.custom"), type = 2 } } });
        await session.Receive(message => message.TryGetProperty("method", out var method) &&
            method.GetString() == "textDocument/publishDiagnostics" && message.GetProperty("params").GetProperty("uri").GetString() == session.Uri("new.custom") &&
            message.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        await session.Stop();
    }

    [Theory]
    [InlineData("obj/generated.gf")]
    [InlineData("trace.log")]
    [InlineData("README.md")]
    public async Task IrrelevantFileNotificationsDoNotCancelPendingCompletion(string changedPath)
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("main.gf", "int Answer() => 42;");
        await session.Diagnostics("main.gf", 1);
        string changed = "int Answer() { int local = 42; return local; }\n" + string.Join("\n",
            Enumerable.Range(0, 400).Select(index => $"struct Item{index} {{ public int Value; }}"));
        await session.Change("main.gf", changed, 2);
        await session.Send(new { jsonrpc = "2.0", id = 1410, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = 37 } } });
        await session.Notify("workspace/didChangeWatchedFiles", new { changes = new[] {
            new { uri = session.Uri(changedPath), type = 2 } } });
        var reply = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1410);
        Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
        Assert.Contains(reply.GetProperty("result").GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "local");
        await session.Stop();
    }

    [Fact]
    public async Task ExplicitSourceUnderGeneratedDirectoryStillUpdatesDiagnostics()
    {
        await using var session = new Session();
        Directory.CreateDirectory(session.File("obj"));
        System.IO.File.WriteAllText(session.File(Workspace.ConfigurationName), """{"sources":["main.gf","obj/generated.gf"]}""");
        System.IO.File.WriteAllText(session.File("obj/generated.gf"), "int Generated() => missing;");
        await session.Initialize();
        await session.Open("main.gf", "int main() => Generated();");
        await session.Diagnostics("obj/generated.gf");
        System.IO.File.WriteAllText(session.File("obj/generated.gf"), "int Generated() => 42;");
        await session.Notify("workspace/didChangeWatchedFiles", new { changes = new[] {
            new { uri = session.Uri("obj/generated.gf"), type = 2 } } });
        var response = await session.Diagnostics("obj/generated.gf");
        Assert.Empty(response.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
    }

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
