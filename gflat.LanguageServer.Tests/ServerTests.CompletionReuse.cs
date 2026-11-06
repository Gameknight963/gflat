using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed partial class ServerTests
{
    [Fact]
    public async Task LocalPrefixCompletionUsesCurrentEditRangesWhileAnalysisIsPending()
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("main.gf", "void Read(){int local=42; }");
        await session.Diagnostics("main.gf", 1);
        const string changed = "void Read(){int local=42; loc}";
        await session.Change("main.gf", changed, 2);
        int offset = changed.IndexOf("loc}", StringComparison.Ordinal) + 3;
        await session.Send(new { jsonrpc = "2.0", id = 1510, method = "textDocument/completion", @params = new {
            textDocument = new { uri = session.Uri("main.gf") }, position = new { line = 0, character = offset } } });
        var reply = await session.Receive(message => message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 1510);
        Assert.False(reply.TryGetProperty("error", out _), reply.ToString());
        var local = Assert.Single(reply.GetProperty("result").GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "local");
        Assert.Equal(offset - 3, local.GetProperty("textEdit").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(offset, local.GetProperty("textEdit").GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        await session.Stop();
    }

    private const string ReuseMain = """
        struct OldValue { public int OldMember; }
        struct NewValue { public int NewMember; }
        int main() { Container receiver = new(); receiver.Member.OldMember; return 0; }
        """;

    private static async Task<string?[]> CompletionReuseLabels(Session session, string text, int firstId)
    {
        int offset = text.IndexOf("receiver.Member.", StringComparison.Ordinal) + "receiver.Member.".Length + 1;
        var source = new SourceFile(session.File("main.gf"), text);
        var span = source.Span(offset);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int request = firstId + attempt;
            await session.Send(new { jsonrpc = "2.0", id = request, method = "textDocument/completion", @params = new {
                textDocument = new { uri = session.Uri("main.gf") },
                position = new { line = span.Line - 1, character = span.Column - 1 }
            } });
            var response = await session.Receive(message => message.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number && id.GetInt32() == request);
            // Real filesystem notifications may supersede the explicit watched-file
            // notification. A cancelled generation must be retried against the next one.
            if (response.TryGetProperty("error", out var error) && error.GetProperty("code").GetInt32() == -32801) continue;
            return response.GetProperty("result").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("label").GetString()).ToArray();
        }
        throw new InvalidOperationException("Repeated filesystem changes prevented completion.");
    }

    [Fact]
    public async Task MemberCompletionDoesNotReuseAReceiverTypeChangedInAnotherOpenDocument()
    {
        await using var session = new Session();
        File.WriteAllText(session.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"types.gf\"]}");
        const string originalTypes = "struct Container { public OldValue Member; }";
        File.WriteAllText(session.File("types.gf"), originalTypes);
        await session.Initialize();
        await session.Open("main.gf", ReuseMain);
        await session.Diagnostics("main.gf", 1);
        await session.Open("types.gf", originalTypes);
        await session.Diagnostics("types.gf", 1);

        await session.Change("types.gf", "struct Container { public NewValue Member; }", 2);
        string changedMain = ReuseMain.Replace("Member.OldMember", "Member.N", StringComparison.Ordinal);
        await session.Change("main.gf", changedMain, 2);
        var labels = await CompletionReuseLabels(session, changedMain, 840);
        Assert.Contains("NewMember", labels);
        Assert.DoesNotContain("OldMember", labels);
        await session.Stop();
    }

    [Fact]
    public async Task MemberCompletionDoesNotReuseAReceiverFromThePreviousConfiguredSourceGraph()
    {
        await using var session = new Session();
        string configuration = session.File(Workspace.ConfigurationName);
        File.WriteAllText(configuration, "{\"sources\":[\"main.gf\",\"old.gf\"]}");
        File.WriteAllText(session.File("old.gf"), "struct Container { public OldValue Member; }");
        File.WriteAllText(session.File("new.gf"), "struct Container { public NewValue Member; }");
        await session.Initialize();
        await session.Open("main.gf", ReuseMain);
        await session.Diagnostics("main.gf", 1);

        File.WriteAllText(configuration, "{\"sources\":[\"main.gf\",\"new.gf\"]}");
        await session.Notify("workspace/didChangeWatchedFiles", new { changes = new[] { new { uri = Workspace.FileUri(configuration), type = 2 } } });
        string changedMain = ReuseMain.Replace("Member.OldMember", "Member.N", StringComparison.Ordinal);
        await session.Change("main.gf", changedMain, 2);
        var labels = await CompletionReuseLabels(session, changedMain, 850);
        Assert.Contains("NewMember", labels);
        Assert.DoesNotContain("OldMember", labels);
        await session.Stop();
    }
}
