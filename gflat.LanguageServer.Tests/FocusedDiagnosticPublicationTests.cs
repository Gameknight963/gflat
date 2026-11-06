using System.Reflection;
using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class FocusedDiagnosticPublicationTests
{
    [Fact]
    public async Task FocusedPassClearsCompletedDocumentWithoutClearingUnopenedDiagnostics()
    {
        using var output = new MemoryStream();
        var server = new LanguageServer(Stream.Null, output, TextWriter.Null);
        string main = Path.GetFullPath("focused-diagnostics.gf"), other = Path.GetFullPath("unopened-diagnostics.gf");
        var issue = new EditorDiagnostic(new(new(0, 0), new(0, 1)), 1, "TEST", "gflat", "error");
        var publish = typeof(LanguageServer).GetMethod("PublishDiagnostics", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task Send(Analysis analysis) => (Task)publish.Invoke(server, [analysis])!;
        await Send(new(new(Workspace.Paths) { [main] = [issue], [other] = [issue] }, []));
        await Send(new(new(Workspace.Paths) { [main] = [], [other] = [] }, [], IsPartial: true, CompletedDiagnosticPaths: [main]));
        output.Position = 0;
        var messages = new List<JsonElement>();
        while (output.Position < output.Length)
        {
            using var json = JsonDocument.Parse((await Protocol.ReadAsync(output, default))!);
            messages.Add(json.RootElement.Clone());
        }
        Assert.Equal(3, messages.Count);
        Assert.Equal(Workspace.FileUri(main), messages[^1].GetProperty("params").GetProperty("uri").GetString());
        Assert.Equal(0, messages[^1].GetProperty("params").GetProperty("diagnostics").GetArrayLength());
    }
}
