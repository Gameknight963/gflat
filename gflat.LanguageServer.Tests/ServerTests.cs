using System.Diagnostics;
using System.Text;
using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ServerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task InvalidWorkspaceSymbolQueryDoesNotTerminateServer(object? query)
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Open("symbol.gf", "int Answer() => 42;");
        await s.Diagnostics("symbol.gf", 1);
        await s.Send(new { jsonrpc = "2.0", id = 753, method = "workspace/symbol", @params = new { query = JsonSerializer.SerializeToElement(query) } });
        var error = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 753);
        Assert.Equal(-32602, error.GetProperty("error").GetProperty("code").GetInt32());
        await s.Send(new { jsonrpc = "2.0", id = 754, method = "workspace/symbol", @params = new { query = "Answer" } });
        var result = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 754);
        Assert.Single(result.GetProperty("result").EnumerateArray());
        await s.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameReturnsNegotiatedEditsForQueuedUnsavedText(bool versioned)
    {
        await using var s = new Session();
        await s.Initialize(clientCapabilities: new { workspace = new { workspaceEdit = new { documentChanges = versioned } } });
        await s.Open("rename.gf", "int F(int value) => value;");
        await s.Diagnostics("rename.gf", 1);
        const string changed = "int F(int value) { value++; nameof(value); return value; } // value";
        await s.Change("rename.gf", changed, 2);
        var position = new { line = 0, character = changed.IndexOf("value", StringComparison.Ordinal) };
        await s.Send(new { jsonrpc = "2.0", id = 750, method = "textDocument/prepareRename", @params = new { textDocument = new { uri = s.Uri("rename.gf") }, position } });
        var prepared = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 750);
        Assert.Equal("value", prepared.GetProperty("result").GetProperty("placeholder").GetString());
        await s.Send(new { jsonrpc = "2.0", id = 751, method = "textDocument/rename", @params = new { textDocument = new { uri = s.Uri("rename.gf") }, position, newName = "renamed" } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 751);
        var result = response.GetProperty("result");
        JsonElement edits;
        if (versioned) {
            var change = Assert.Single(result.GetProperty("documentChanges").EnumerateArray());
            Assert.Equal(s.Uri("rename.gf"), change.GetProperty("textDocument").GetProperty("uri").GetString());
            Assert.Equal(2, change.GetProperty("textDocument").GetProperty("version").GetInt32());
            edits = change.GetProperty("edits");
        } else edits = result.GetProperty("changes").GetProperty(s.Uri("rename.gf"));
        Assert.Equal(4, edits.GetArrayLength());
        Assert.All(edits.EnumerateArray(), edit => {
            var range = JsonSerializer.Deserialize<TextRange>(edit.GetProperty("range"), Protocol.JsonOptions)!;
            int start = Workspace.Offset(changed, JsonSerializer.SerializeToElement(range.Start, Protocol.JsonOptions));
            int end = Workspace.Offset(changed, JsonSerializer.SerializeToElement(range.End, Protocol.JsonOptions));
            Assert.Equal("value", changed[start..end]);
            Assert.Equal("renamed", edit.GetProperty("newText").GetString());
        });
        await s.Stop();
    }

    [Fact]
    public async Task WorkspaceSymbolSearchUsesUnopenedAndQueuedUnsavedFiles()
    {
        await using var s = new Session();
        File.WriteAllText(s.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        File.WriteAllText(s.File("helper.gf"), "int LibraryAnswer() => 42;");
        await s.Initialize();
        await s.Open("main.gf", "int Original() => 1;");
        await s.Diagnostics("main.gf", 1);
        await s.Change("main.gf", "int ChangedAnswer() => 2;", 2);
        await s.Send(new { jsonrpc = "2.0", id = 752, method = "workspace/symbol", @params = new { query = "Answer" } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 752);
        var result = response.GetProperty("result");
        Assert.Equal(2, result.GetArrayLength());
        Assert.Equal(new[] { "ChangedAnswer", "LibraryAnswer" }, result.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());
        Assert.Contains(result.EnumerateArray(), item => item.GetProperty("location").GetProperty("uri").GetString() == s.Uri("helper.gf"));
        await s.Stop();
    }

    [Fact]
    public async Task DocumentHighlightsUseQueuedUnsavedSnapshot()
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Open("highlight.gf", "int F() { int value = 1; return value; }");
        await s.Diagnostics("highlight.gf", 1);
        const string changed = "int F() { int value = 1; value++; return value; }";
        await s.Change("highlight.gf", changed, 2);
        await s.Send(new { jsonrpc = "2.0", id = 730, method = "textDocument/documentHighlight", @params = new {
            textDocument = new { uri = s.Uri("highlight.gf") }, position = new { line = 0, character = changed.IndexOf("value", StringComparison.Ordinal) } } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 730);
        var result = response.GetProperty("result");
        Assert.Equal(3, result.GetArrayLength());
        Assert.All(result.EnumerateArray(), item => {
            Assert.True(item.TryGetProperty("range", out _));
            Assert.False(item.TryGetProperty("uri", out _));
            Assert.False(item.TryGetProperty("kind", out _));
        });
        await s.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentSymbolsRespectClientHierarchyCapability(bool hierarchical)
    {
        await using var s = new Session();
        await s.Initialize(clientCapabilities: new { textDocument = new { documentSymbol = new { hierarchicalDocumentSymbolSupport = hierarchical } } });
        await s.Open("outline.gf", "struct S { public int value; }");
        await s.Diagnostics("outline.gf", 1);
        await s.Change("outline.gf", "struct Changed { public int value; }", 2);
        await s.Send(new { jsonrpc = "2.0", id = 720, method = "textDocument/documentSymbol", @params = new { textDocument = new { uri = s.Uri("outline.gf") } } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == 720);
        var result = response.GetProperty("result");
        Assert.Equal(hierarchical ? 1 : 2, result.GetArrayLength());
        Assert.Equal("Changed", result[0].GetProperty("name").GetString());
        Assert.Equal(hierarchical, result[0].TryGetProperty("children", out _));
        Assert.Equal(!hierarchical, result[0].TryGetProperty("location", out _));
        await s.Stop();
    }

    [Fact]
    public async Task ReferencesUseUnsavedProjectSnapshotsAndDeclarationOption()
    {
        await using var s = new Session();
        System.IO.File.WriteAllText(s.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        System.IO.File.WriteAllText(s.File("helper.gf"), "int Answer() => 1;");
        await s.Initialize();
        const string main = "int F() => Answer(); int G() => Answer();";
        await s.Open("main.gf", main);
        await s.Diagnostics("main.gf", 1);
        await s.Open("helper.gf", "int Answer() => 42;");
        await s.Diagnostics("helper.gf", 1);
        int id = 710;
        async Task<JsonElement> References(bool includeDeclaration)
        {
            int request = id++;
            await s.Send(new { jsonrpc = "2.0", id = request, method = "textDocument/references", @params = new {
                textDocument = new { uri = s.Uri("main.gf") }, position = new { line = 0, character = main.IndexOf("Answer", StringComparison.Ordinal) },
                context = new { includeDeclaration } } });
            return (await s.Receive(x => x.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == request)).GetProperty("result");
        }
        Assert.Equal(2, (await References(false)).GetArrayLength());
        var all = await References(true);
        Assert.Equal(3, all.GetArrayLength());
        Assert.Contains(all.EnumerateArray(), location => location.GetProperty("uri").GetString() == s.Uri("helper.gf"));
        await s.Change("main.gf", "int F() => Answer();", 2);
        await s.Diagnostics("main.gf", 2);
        Assert.Single((await References(false)).EnumerateArray());
        await s.Stop();
    }

    [Fact]
    public async Task BrokenProjectDependencyPreservesEditorServicesAcrossEdits()
    {
        await using var s = new Session();
        System.IO.File.WriteAllText(s.File(Workspace.ConfigurationName), "{\"sources\":[\"main.gf\",\"helper.gf\"]}");
        const string healthy = "int answer() => 42;";
        System.IO.File.WriteAllText(s.File("helper.gf"), healthy);
        await s.Initialize();
        await s.Open("main.gf", "int main() => answer();");
        await s.Diagnostics("main.gf", 1);
        await s.Open("helper.gf", healthy);
        await s.Diagnostics("helper.gf", 1);
        int id = 500;
        foreach (string text in new[] { "@\n" + healthy, "int bad = ;\n" + healthy, "int bad() { missing(); }\n" + healthy, healthy })
        {
            await s.Change("helper.gf", text, id);
            await s.Diagnostics("helper.gf", id);
            foreach (string method in new[] { "hover", "definition", "semanticTokens/full" })
            {
                int request = id++;
                await s.Send(new { jsonrpc = "2.0", id = request, method = "textDocument/" + method,
                    @params = new { textDocument = new { uri = s.Uri("main.gf") }, position = new { line = 0, character = 14 } } });
                var response = await s.Receive(x => x.TryGetProperty("id", out var n) && n.ValueKind == JsonValueKind.Number && n.GetInt32() == request);
                Assert.NotEqual(JsonValueKind.Null, response.GetProperty("result").ValueKind);
                if (method == "hover") Assert.Contains("answer", response.GetProperty("result").ToString());
                if (method == "definition") Assert.Contains("helper.gf", response.GetProperty("result").ToString());
                if (method == "semanticTokens/full") Assert.NotEmpty(response.GetProperty("result").GetProperty("data").EnumerateArray());
            }
        }
        await s.Stop();
    }

    [Fact]
    public async Task HoverSettingsUpdateWithoutDocumentEdits()
    {
        await using var s = new Session();
        await s.Initialize();
        const string source = "void Fail() { throw new Exception(); } struct Size { int n; }";
        await s.Open("options.gf", source);
        await s.Diagnostics("options.gf", 1);
        int requestId = 80;
        async Task<string> HoverAt(string word)
        {
            int id = requestId++;
            await s.Send(new { jsonrpc = "2.0", id, method = "textDocument/hover", @params = new {
                textDocument = new { uri = s.Uri("options.gf") }, position = new { line = 0, character = source.IndexOf(word, StringComparison.Ordinal) } } });
            var response = await s.Receive(x => x.TryGetProperty("id", out var n) && n.ValueKind == JsonValueKind.Number && n.GetInt32() == id);
            return response.GetProperty("result").GetProperty("contents").GetProperty("value").GetString()!;
        }
        Assert.DoesNotContain("Known exceptions:", await HoverAt("Fail"));
        Assert.Contains("Size:", await HoverAt("Size"));
        await s.Send(new { jsonrpc = "2.0", method = "workspace/didChangeConfiguration", @params = new {
            settings = new { gflat = new { showKnownExceptions = true, showTypeLayout = false } } } });
        Assert.Contains("Known exceptions:", await HoverAt("Fail"));
        Assert.DoesNotContain("Size:", await HoverAt("Size"));
        await s.Send(new { jsonrpc = "2.0", method = "workspace/didChangeConfiguration", @params = new {
            settings = new { gflat = new { showKnownExceptions = false, showTypeLayout = true } } } });
        Assert.DoesNotContain("Known exceptions:", await HoverAt("Fail"));
        Assert.Contains("Size:", await HoverAt("Size"));
        await s.Stop();
    }

    [Fact]
    public async Task RichHoverAndNoThrowClassificationReachTheClient()
    {
        await using var s = new Session();
        string? catalogFile = Environment.GetEnvironmentVariable("GFLAT_HOVER_CATALOG");
        var icons = catalogFile == null
            ? JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["method.public"] = new { guid = Guid.Parse("ae27a6b0-e345-4288-96df-5eaf394ee369"), id = 1 } })
            : JsonDocument.Parse(System.IO.File.ReadAllText(catalogFile)).RootElement.Clone();
        await s.Initialize(new { visualStudioClassifications = true, hoverIcons = icons, visualStudioNativeHover = true, gflat = new { showKnownExceptions = true } });
        const string source = "public int Read(int count) { throw new Failure(); } class Failure : Exception {} void Safe() nothrow {} void Use() { Read(1); }";
        await s.Open("hover.gf", source);
        await s.Diagnostics("hover.gf", 1);
        await s.Send(new { jsonrpc = "2.0", id = 71, method = "textDocument/hover", @params = new { textDocument = new { uri = s.Uri("hover.gf") }, position = new { line = 0, character = 12 } } });
        var hover = (await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 71)).GetProperty("result");
        Assert.Equal("ContainerElement", hover.GetProperty("_vs_rawContent").GetProperty("_vs_type").GetString());
        string? capture = Environment.GetEnvironmentVariable("GFLAT_HOVER_CAPTURE");
        if (capture != null) System.IO.File.WriteAllText(capture, hover.GetRawText());
        await s.Send(new { jsonrpc = "2.0", id = 73, method = "textDocument/signatureHelp", @params = new { textDocument = new { uri = s.Uri("hover.gf") }, position = new { line = 0, character = source.IndexOf("Read(1", StringComparison.Ordinal) + 5 } } });
        var signature = (await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 73)).GetProperty("result").GetProperty("signatures")[0];
        Assert.Equal("ClassifiedTextElement", signature.GetProperty("_vs_colorizedLabel").GetProperty("_vs_type").GetString());
        if (capture != null) System.IO.File.WriteAllText(capture + ".signature", signature.GetRawText());
        await s.Send(new { jsonrpc = "2.0", id = 72, method = "textDocument/semanticTokens/full", @params = new { textDocument = new { uri = s.Uri("hover.gf") } } });
        var semantic = (await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 72)).GetProperty("result");
        var data = semantic.GetProperty("data").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        int column = 0;
        bool found = false;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (column == source.IndexOf("nothrow", StringComparison.Ordinal))
            { Assert.Equal("modifier", EditorModel.TokenTypes[data[i + 3]]); found = true; }
        }
        Assert.True(found);
        await s.Stop();
    }

    [Fact]
    public async Task VisualStudioUsesItsExistingThemeClassifications()
    {
        await using var s = new Session();
        await s.Send(new { jsonrpc = "2.0", id = 20, method = "initialize", @params = new { capabilities = new { }, initializationOptions = new { visualStudioClassifications = true } } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var id) && id.GetInt32() == 20);
        var capabilities = response.GetProperty("result").GetProperty("capabilities");
        var legend = capabilities.GetProperty("semanticTokensProvider").GetProperty("legend").GetProperty("tokenTypes").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(EditorModel.TokenTypes.Length, legend.Length);
        Assert.Equal("local name", legend[Array.IndexOf(EditorModel.TokenTypes, "variable")]);
        Assert.Equal("keyword - control", legend[Array.IndexOf(EditorModel.TokenTypes, "keyword")]);
        Assert.True(capabilities.GetProperty("hoverProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("definitionProvider").GetBoolean());
        await s.Stop();
    }

    [Theory]
    [InlineData("int main() => missing;", 0, 14, 0, 21)]
    [InlineData("// 😀\r\nint main() { int n = (true\r\n || false); return 0; }", 1, 21, 2, 10)]
    [InlineData("int main() { return 0 }", 0, 22, 0, 22)]
    public async Task PublishedDiagnosticsHaveExactRanges(string source, int startLine, int startColumn, int endLine, int endColumn)
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("ranges.gf", source);
        var diagnostics = (await session.Diagnostics("ranges.gf", 1)).GetProperty("params").GetProperty("diagnostics");
        var range = diagnostics.EnumerateArray().First(d => d.GetProperty("severity").GetInt32() == 1).GetProperty("range");
        Assert.Equal(startLine, range.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(startColumn, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(endLine, range.GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(endColumn, range.GetProperty("end").GetProperty("character").GetInt32());
        await session.Stop();
    }

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
        public async Task Initialize(object? initializationOptions = null, object? clientCapabilities = null)
        {
            await Send(new { jsonrpc = "2.0", id = "initialize", method = "initialize", @params = new { rootUri = Workspace.FileUri(Root), capabilities = clientCapabilities ?? new { }, initializationOptions } });
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
    public async Task EditorRequestsUseTheLatestUnsavedSnapshot()
    {
        await using var s = new Session();
        await s.Initialize();
        const string original = "int main() { int before = 1; return before; }";
        const string changed = "int main() { int after = 1; return after; }";
        await s.Open("queries.gf", original);
        await s.Diagnostics("queries.gf", 1);
        await s.Change("queries.gf", changed, 2);
        // Deliberately request before the debounced analysis has finished.
        await s.Send(new { jsonrpc = "2.0", id = 10, method = "textDocument/completion", @params = new { textDocument = new { uri = s.Uri("queries.gf") }, position = new { line = 0, character = changed.IndexOf("return", StringComparison.Ordinal) } } });
        var response = await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 10);
        var labels = response.GetProperty("result").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToArray();
        Assert.Contains("after", labels);
        Assert.DoesNotContain("before", labels);
        await s.Send(new { jsonrpc = "2.0", id = 11, method = "textDocument/hover", @params = new { textDocument = new { uri = s.Uri("queries.gf") }, position = new { line = 0, character = changed.LastIndexOf("after", StringComparison.Ordinal) } } });
        var hover = await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 11);
        Assert.Contains("int after", hover.GetProperty("result").ToString());
        await s.Send(new { jsonrpc = "2.0", id = 12, method = "textDocument/semanticTokens/full", @params = new { textDocument = new { uri = s.Uri("queries.gf") } } });
        var semantic = await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 12);
        Assert.NotEmpty(semantic.GetProperty("result").GetProperty("data").EnumerateArray());
        await s.Stop();
    }

    [Theory]
    [InlineData(true, -32800)]
    [InlineData(false, -32801)]
    public async Task PendingEditorRequestsCanBeCancelledOrInvalidated(bool cancel, int code)
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Open("cancel.gf", "int main() => 0;");
        await s.Send(new { jsonrpc = "2.0", id = 14, method = "textDocument/hover", @params = new { textDocument = new { uri = s.Uri("cancel.gf") }, position = new { line = 0, character = 4 } } });
        if (cancel) await s.Notify("$/cancelRequest", new { id = 14 });
        else await s.Change("cancel.gf", "int main() => 1;", 2);
        var response = await s.Receive(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 14);
        Assert.Equal(code, response.GetProperty("error").GetProperty("code").GetInt32());
        await s.Stop();
    }

    [Fact]
    public async Task InterpolationDiagnosticsRecoverAfterEditingHoles()
    {
        await using var session = new Session();
        await session.Initialize();
        const string declarations = """
            struct Text : ITextWriter {
                public static Text operator t""(readonly(char)* p, nuint n) { return new Text(); }
                public void Write(readonly(char)* p, nuint n) { }
            }
            struct Value : IFormattable {
                public readonly void Format(ITextWriter* output) { output.Write("", 0); }
            }
            """;
        string valid = declarations + "\nint main() { t$\"{new Value()}\"; return 0; }";
        await session.Open("interpolation.gf", declarations + "\nint main() { t$\"{missing}\"; return 0; }");
        Assert.NotEmpty((await session.Diagnostics("interpolation.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Change("interpolation.gf", valid, 2);
        Assert.Empty((await session.Diagnostics("interpolation.gf", 2)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Change("interpolation.gf", declarations + "\nint main() { t$\"{", 3);
        Assert.NotEmpty((await session.Diagnostics("interpolation.gf", 3)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Change("interpolation.gf", valid, 4);
        Assert.Empty((await session.Diagnostics("interpolation.gf", 4)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
    }

    [Fact]
    public async Task ReadOnlyTypeDiagnosticsRecoverAfterRemovingTheWrite()
    {
        await using var session = new Session();
        await session.Initialize();
        const string source = "struct S { public int* data; public S(int* p) { data = p; } } int main() { int n = 0; readonly(S) s = new S(&n); s.data[0] = 1; return 0; }";
        await session.Open("readonly.gf", source);
        var broken = await session.Diagnostics("readonly.gf", 1);
        Assert.Contains("readonly", broken.GetProperty("params").GetProperty("diagnostics").ToString());
        await session.Change("readonly.gf", source.Replace("s.data[0] = 1;", ""), 2);
        Assert.Empty((await session.Diagnostics("readonly.gf", 2)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
    }

    [Theory]
    [InlineData("struct S { int* p; public S(int* q) { } }", "{ }", "{ p = q; }")]
    [InlineData("int main() { int*? p = null; return *p; }", "return *p;", "int* q = (int*)p; return *q;")]
    [InlineData("enum E { A = 1 } int main() => E::A;", "=> E::A;", "=> (int)E::A;")]
    [InlineData("struct S { public static S operator s\"\"(readonly(char)* p, ulong n) => new S(); }", "ulong n", "nuint n")]
    [InlineData("alias GC = Collector; static class Collector { public static void* Allocate(ulong n, nuint alignment) => (void*)1; public static void ReferenceAdded(void* slot) nothrow {} public static void ReferenceRemoved(void* slot) nothrow {} } int main() { int^ value = new^ int(); return 0; }", "ulong n", "nuint n")]
    public async Task ContractDiagnosticsRecoverAfterAnEdit(string source, string before, string after)
    {
        await using var session = new Session();
        await session.Initialize();
        await session.Open("contracts.gf", source);
        Assert.NotEmpty((await session.Diagnostics("contracts.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Change("contracts.gf", source.Replace(before, after), 2);
        Assert.Empty((await session.Diagnostics("contracts.gf", 2)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
    }

    [Fact]
    public async Task QualifiedNamespacesAndIndexersRecoverAcrossUnsavedFiles()
    {
        await using var session = new Session();
        System.IO.File.WriteAllText(session.File("app.gfproj"), gflat.TestSupport.ProjectFileFixture.Xml());
        await session.Initialize();
        const string library = "namespace Lib::Collections; public struct S { public int this[int i] => i; }";
        await session.Open("library.gf", library);
        Assert.Empty((await session.Diagnostics("library.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Open("use.gf", "using Lib::Collections; int main() { S s = new S(); return s[1, 2]; }");
        Assert.NotEmpty((await session.Diagnostics("use.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Change("use.gf", "using Lib::Collections; int main() { S s = new S(); return s[42]; }", 2);
        Assert.Empty((await session.Diagnostics("use.gf", 2)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        await session.Stop();
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
    public async Task IncompleteReturnBeforeMemberAssignmentReportsErrorAndRecovers()
    {
        await using var session = new Session();
        await session.Initialize();
        const string source = """
            extern int printf(readonly(char)* fmt, ...);
            struct Cool {
                public int x;
                ~Cool() { printf("destructor ran"); }
            }
            int main() {
                Cool* l = new* Cool();
                defer delete l;
                retu
                l.x = 2;
                printf("The number is: %d\n", 2);
                return 0;
            }
            """;
        await session.Open("program.gf", source);
        var broken = (await session.Diagnostics("program.gf", 1)).GetProperty("params").GetProperty("diagnostics");
        Assert.Contains(broken.EnumerateArray(), d => d.GetProperty("severity").GetInt32() == 1);
        await session.Change("program.gf", source.Replace("retu\r\n", "\r\n").Replace("retu\n", "\n"), 2);
        Assert.Empty((await session.Diagnostics("program.gf", 2)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
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

    [Theory]
    [InlineData("}")]
    [InlineData(")")]
    [InlineData("]")]
    [InlineData(",")]
    [InlineData("class C { ) }")]
    [InlineData("namespace N { ] }")]
    [InlineData("int main() { return 0; } }")]
    public async Task UnexpectedDeclarationDelimiterDoesNotHangAnalysis(string source)
    {
        await using var s = new Session();
        await s.Initialize();
        await s.Open("broken.gf", source);
        var diagnostics = (await s.Diagnostics("broken.gf", 1)).GetProperty("params").GetProperty("diagnostics");
        Assert.Contains(diagnostics.EnumerateArray(), d => d.GetProperty("severity").GetInt32() == 1);
        await s.Change("broken.gf", "int main() => 0;", 2);
        Assert.Empty((await s.Diagnostics("broken.gf", 2)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
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
    public async Task ProjectGlobsAndXmlChangesReloadWithoutRestartingServer()
    {
        await using var s = new Session();
        string project = s.File("app.gfproj");
        string xml = gflat.TestSupport.ProjectFileFixture.Xml();
        System.IO.File.WriteAllText(project, xml);
        await s.Initialize();
        await s.Open("main.gf", "int main() => answer();");
        Assert.Contains("answer", (await s.Diagnostics("main.gf", 1)).ToString());
        System.IO.File.WriteAllText(s.File("helper.gf"), "int answer() => 42;");
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        System.IO.File.WriteAllText(project, "<GflatProject");
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Any(d => d.GetProperty("code").GetString() == "GFPROJ"));
        System.IO.File.WriteAllText(project, xml);
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        await s.Stop();

        bool IsMainDiagnostics(JsonElement x) => x.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
            x.GetProperty("params").GetProperty("uri").GetString() == s.Uri("main.gf");
    }

    [Fact]
    public async Task CreatingAnAdjacentProjectUpdatesAnAlreadyOpenLooseFile()
    {
        await using var s = new Session();
        System.IO.File.WriteAllText(s.File("helper.gf"), "int answer() => 42;");
        await s.Initialize();
        await s.Open("main.gf", "int main() => answer();");
        Assert.Contains("answer", (await s.Diagnostics("main.gf", 1)).ToString());
        System.IO.File.WriteAllText(s.File("app.gfproj"), gflat.TestSupport.ProjectFileFixture.Xml());
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        await s.Stop();

        bool IsMainDiagnostics(JsonElement x) => x.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
            x.GetProperty("params").GetProperty("uri").GetString() == s.Uri("main.gf");
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public async Task CreatingSharedBuildInputsReloadsAndRecoversFromMalformedXml(string sharedName)
    {
        await using var s = new Session();
        Directory.CreateDirectory(s.File("app"));
        System.IO.File.WriteAllText(s.File("app/app.gfproj"), gflat.TestSupport.ProjectFileFixture.Xml());
        System.IO.File.WriteAllText(s.File("shared.gf"), "int answer() => 42;");
        await s.Initialize();
        await s.Open("app/main.gf", "int main() => answer();");
        Assert.Contains("answer", (await s.Diagnostics("app/main.gf", 1)).ToString());
        System.IO.File.WriteAllText(s.File(sharedName), "<Project");
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Any(d => d.GetProperty("code").GetString() == "GFPROJ"));
        System.IO.File.WriteAllText(s.File(sharedName), """
            <Project><ItemGroup><Compile Include="$(MSBuildThisFileDirectory)shared.gf" /></ItemGroup></Project>
            """);
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        System.IO.File.Delete(s.File(sharedName));
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Any(d => d.GetProperty("message").GetString()!.Contains("answer")));
        await s.Stop();

        bool IsMainDiagnostics(JsonElement x) => x.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
            x.GetProperty("params").GetProperty("uri").GetString() == s.Uri("app/main.gf");
    }

    [Fact]
    public async Task SharedNoStdLibChangesRemainInEffectDuringInvalidProjectEdits()
    {
        await using var s = new Session();
        Directory.CreateDirectory(s.File("app"));
        System.IO.File.WriteAllText(s.File("app/app.gfproj"), gflat.TestSupport.ProjectFileFixture.WithImports("<Project />"));
        await s.Initialize();
        await s.Open("app/main.gf", "int main() { Exception*? error = null; return 0; }");
        Assert.Empty((await s.Diagnostics("app/main.gf", 1)).GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        System.IO.File.WriteAllText(s.File("Directory.Build.props"), "<Project><PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup></Project>");
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Any(d => d.GetProperty("message").GetString()!.Contains("Exception")));
        System.IO.File.WriteAllText(s.File("Directory.Build.props"), "<Project");
        var broken = await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").EnumerateArray().Any(d => d.GetProperty("code").GetString() == "GFPROJ"));
        Assert.Contains(broken.GetProperty("params").GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("message").GetString()!.Contains("Exception"));
        System.IO.File.Delete(s.File("Directory.Build.props"));
        await s.Receive(x => IsMainDiagnostics(x) && x.GetProperty("params").GetProperty("diagnostics").GetArrayLength() == 0);
        await s.Stop();

        bool IsMainDiagnostics(JsonElement x) => x.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
            x.GetProperty("params").GetProperty("uri").GetString() == s.Uri("app/main.gf");
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
        await s.Send(new { jsonrpc = "2.0", id = 4, method = "textDocument/notImplemented", @params = new { } });
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
    [InlineData("int main() { readonly(char)* s = \"")]
    [InlineData("class C { int P { get")]
    [InlineData("int main() { foo(")]
    [InlineData("int main() { int x = new")]
    [InlineData("struct S<T")]
    [InlineData("using A::")]
    [InlineData("namespace std::")]
    [InlineData("namespace std; struct S { public int this[")]
    [InlineData("namespace std::Collections { struct S { public int this[int i] { get")]
    [InlineData("struct S { public int this[int i] => i; } int main() { S s = new S(); return s[0,")]
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
