using System.Diagnostics;
using System.Text;
using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ServerTests
{
    private sealed class Session : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "gflat_lsp_" + Guid.NewGuid().ToString("N"));
        public Process Process { get; }
        private readonly Task<string> stderr;
        public Session()
        {
            Directory.CreateDirectory(Root);
            string? packaged = Environment.GetEnvironmentVariable("GFLAT_LSP_EXECUTABLE");
            var info = new ProcessStartInfo(packaged ?? "dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            if (packaged == null) info.ArgumentList.Add(typeof(global::gflat.LanguageServer.Program).Assembly.Location);
            Process = Process.Start(info)!;
            stderr = Process.StandardError.ReadToEndAsync();
        }
        public string File(string name) => Path.Combine(Root, name);
        public string Uri(string name) => Workspace.FileUri(File(name));
        public Task Send(object message) => Protocol.WriteAsync(Process.StandardInput.BaseStream, message);
        public Task Notify(string method, object parameters) => Send(new { jsonrpc = "2.0", method, @params = parameters });
        public async Task<JsonElement> Receive(Func<JsonElement, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (await Protocol.ReadAsync(Process.StandardOutput.BaseStream, timeout.Token) is byte[] data)
            {
                using var json = JsonDocument.Parse(data);
                if (predicate(json.RootElement)) return json.RootElement.Clone();
            }
            throw new Exception("Language server ended: " + await stderr);
        }
        public async Task Initialize()
        {
            await Send(new { jsonrpc = "2.0", id = "initialize", method = "initialize", @params = new { rootUri = Workspace.FileUri(Root), capabilities = new { } } });
            var response = await Receive(x => x.TryGetProperty("id", out var id) && id.GetString() == "initialize");
            Assert.Equal(1, response.GetProperty("result").GetProperty("capabilities").GetProperty("textDocumentSync").GetProperty("change").GetInt32());
            await Notify("initialized", new { });
        }
        public Task Open(string name, string text) => Notify("textDocument/didOpen", new { textDocument = new { uri = Uri(name), languageId = "gflat", version = 1, text } });
        public Task Change(string name, string text, int version) => Notify("textDocument/didChange", new { textDocument = new { uri = Uri(name), version }, contentChanges = new[] { new { text } } });
        public Task<JsonElement> Diagnostics(string name, int? version = null) => Receive(x =>
            x.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
            x.GetProperty("params").GetProperty("uri").GetString() == Uri(name) &&
            (version == null || x.GetProperty("params").TryGetProperty("version", out var v) && v.GetInt32() == version));
        public async Task Stop()
        {
            await Send(new { jsonrpc = "2.0", id = 99, method = "shutdown" });
            var response = await Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 99);
            Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
            await Notify("exit", new { });
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, Process.ExitCode);
            Assert.Equal("", await stderr);
        }
        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync();
            Process.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsUnsavedErrorsAndClearsThemAfterAnEdit()
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("hello world.gf", "int main() { return missing; }");
        var broken = await session.Diagnostics("hello world.gf", 1);
        Assert.Contains("missing", broken.ToString());
        await session.Change("hello world.gf", "int main() => 42;", 2);
        var fixedCode = await session.Diagnostics("hello world.gf", 2);
        Assert.Empty(fixedCode.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
    }

    [Fact]
    public async Task WorkspaceUsesUnsavedDependenciesAndRevertsToDiskOnClose()
    {
        await using var s = new Session();
        System.IO.File.WriteAllText(s.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        System.IO.File.WriteAllText(s.File("helper.gf"), "int answer() => 42;");
        await s.Initialize();
        await s.Open("main.gf", "int main() => answer();");
        await s.Open("helper.gf", "int other() => 1;");
        Assert.Contains("answer", (await s.Diagnostics("main.gf", 1)).ToString());
        await s.Notify("textDocument/didClose", new { textDocument = new { uri = s.Uri("helper.gf") } });
        var result = await s.Diagnostics("main.gf", 1);
        Assert.Empty(result.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await s.Stop();
    }

    [Fact]
    public async Task DiskChangesTriggerWorkspaceAnalysis()
    {
        await using var s = new Session();
        System.IO.File.WriteAllText(s.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        System.IO.File.WriteAllText(s.File("helper.gf"), "int answer() => 42;");
        await s.Initialize();
        await s.Open("main.gf", "int main() => answer();");
        Assert.Empty((await s.Diagnostics("main.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        System.IO.File.WriteAllText(s.File("helper.gf"), "int other() => 1;");
        Assert.Contains("answer", (await s.Diagnostics("main.gf", 1)).ToString());
        await s.Stop();
    }

    [Fact]
    public async Task RapidEditsPublishLatestVersionAndIgnoreOlderVersions()
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Open("main.gf", "int main() => missing;");
        for (int i = 2; i < 20; i++) await s.Change("main.gf", "int main() => missing;", i);
        await s.Change("main.gf", "int main() => 0;", 20);
        await s.Change("main.gf", "int main() => missing;", 19);
        Assert.Empty((await s.Diagnostics("main.gf", 20)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await s.Stop();
    }

    [Fact]
    public async Task LooseFilesAreIndependentAndCloseClearsDiagnostics()
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Open("a.gf", "int main() => missing;");
        await s.Open("b.gf", "int main() => 0;");
        var results = new[] { await s.Receive(IsDiagnostics), await s.Receive(IsDiagnostics) };
        Assert.Single(results.Single(x => x.GetProperty("params").GetProperty("uri").GetString() == s.Uri("a.gf")).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        Assert.Empty(results.Single(x => x.GetProperty("params").GetProperty("uri").GetString() == s.Uri("b.gf")).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await s.Notify("textDocument/didClose", new { textDocument = new { uri = s.Uri("a.gf") } });
        Assert.Empty((await s.Diagnostics("a.gf")).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await s.Stop();
        static bool IsDiagnostics(JsonElement x) => x.TryGetProperty("method", out var m) && m.GetString() == "textDocument/publishDiagnostics";
    }

    [Fact]
    public async Task UnsupportedRequestsReturnAnErrorAndServerStaysAlive()
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Send(new { jsonrpc = "2.0", id = 4, method = "textDocument/hover", @params = new { } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 4);
        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
        await s.Stop();
    }

    [Theory]
    [InlineData("class C {")]
    [InlineData("int main() { return")]
    [InlineData("int main() { int x = ; }")]
    [InlineData("struct S { int P { get => ; } }")]
    [InlineData("int main() { /*")]
    [InlineData("int main() { readonly char* s = \"")]
    [InlineData("class C { int P { get")]
    [InlineData("int main() { foo(")]
    [InlineData("int main() { int x = new")]
    [InlineData("struct S<T")]
    [InlineData("using A::")]
    public void IncompleteCodeReturnsSourceDiagnostics(string source)
    {
        var result = Workspace.Analyze(null, new Dictionary<string, OpenDocument> { ["broken.gf"] = new("broken.gf", source, 1) });
        Assert.NotEmpty(result.Diagnostics["broken.gf"]);
        Assert.DoesNotContain(result.Diagnostics["broken.gf"], d => d.Code == "GFLS0002");
    }

    [Fact]
    public void IncrementalChangesUseUtf16AndCrLfPositions()
    {
        using var changes = JsonDocument.Parse("""[{"range":{"start":{"line":0,"character":3},"end":{"line":0,"character":4}},"text":"Z"},{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":1}},"text":"Q"}]""");
        Assert.Equal("a😀Z\r\nQ", Workspace.ApplyChanges("a😀b\r\nc", changes.RootElement));
        var source = new SourceFile("a.gf", "a😀b\r\nc");
        Assert.Equal(new TextRange(new(0, 3), new(0, 4)), Workspace.ToRange(source.Span(3, 1)));
    }

    [Fact]
    public async Task FramingCountsUtf8BytesAndReadsConsecutiveMessages()
    {
        using var stream = new MemoryStream();
        await Protocol.WriteAsync(stream, new { text = "😀é" });
        await Protocol.WriteAsync(stream, new { text = "second" });
        stream.Position = 0;
        using var first = JsonDocument.Parse((await Protocol.ReadAsync(stream))!);
        using var second = JsonDocument.Parse((await Protocol.ReadAsync(stream))!);
        Assert.Equal("😀é", first.RootElement.GetProperty("text").GetString());
        Assert.Equal("second", second.RootElement.GetProperty("text").GetString());
        Assert.Null(await Protocol.ReadAsync(stream));
    }

    [Theory]
    [InlineData("Content-Length: -1\r\n\r\n")]
    [InlineData("Content-Length: 9000000\r\n\r\n")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    public async Task InvalidFrameLengthsAreRejected(string frame)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(frame));
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync(stream));
    }
}
