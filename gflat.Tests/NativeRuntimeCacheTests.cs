using System.Diagnostics;
using System.Text.Json;

namespace gflat.Tests;

public sealed class NativeRuntimeCacheTests
{
    [Fact]
    public void CompilerReplacementInvalidatesEvenWithPreservedSizeAndTimestamp()
    {
        using var fixture = new Fixture();
        string compiler = Path.Combine(fixture.Root, "compiler.exe");
        File.WriteAllText(compiler, "compiler one");
        DateTime modified = File.GetLastWriteTimeUtc(compiler);
        string before = NativeRuntimeCache.BuildKey(compiler, fixture.Source, "fixture", []);
        File.WriteAllText(compiler, "compiler two");
        File.SetLastWriteTimeUtc(compiler, modified);
        Assert.NotEqual(before, NativeRuntimeCache.BuildKey(compiler, fixture.Source, "fixture", []));
    }

    [Fact]
    public void ReuseInvalidatesHeadersSourceOptionsAndCorruptObjects()
    {
        using var fixture = new Fixture();
        string original = fixture.Build();
        Assert.Equal(original, fixture.Build());
        File.WriteAllText(fixture.Header, "#define VALUE 127\n");
        string headerChanged = fixture.Build();
        Assert.NotEqual(original, headerChanged);
        Assert.Equal(headerChanged, fixture.Build());
        DateTime sourceTime = File.GetLastWriteTimeUtc(fixture.Source);
        File.AppendAllText(fixture.Source, "\n// Source input changed\n");
        File.SetLastWriteTimeUtc(fixture.Source, sourceTime);
        string sourceChanged = fixture.Build();
        Assert.NotEqual(headerChanged, sourceChanged);
        Assert.NotEqual(sourceChanged, fixture.Build("-DCACHE_OPTION=1"));
        File.WriteAllBytes(sourceChanged, [1, 2, 3]);
        string repaired = fixture.Build();
        Assert.NotEqual(sourceChanged, repaired);
        Assert.True(new FileInfo(repaired).Length > 3);
        Assert.Equal(repaired, fixture.Build());
    }

    [Fact]
    public void FailedCompilationAndMalformedManifestAreRebuilt()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Source, "this is invalid C");
        Assert.Throws<InvalidOperationException>(() => fixture.Build());
        Assert.Empty(Directory.GetFiles(fixture.Cache, "current.json", SearchOption.AllDirectories));
        File.WriteAllText(fixture.Source, "#include \"value.h\"\nint cached_value(void) { return VALUE; }\n");
        string original = fixture.Build();
        string manifest = Assert.Single(Directory.GetFiles(fixture.Cache, "current.json", SearchOption.AllDirectories));
        File.WriteAllText(manifest, "unfinished {");
        string repaired = fixture.Build();
        Assert.NotEqual(original, repaired);
        Assert.Equal(repaired, fixture.Build());
    }

    [Fact]
    public void UnavailablePersistentDirectoryFallsBackToAnOrdinaryBuild()
    {
        using var fixture = new Fixture();
        string blocked = Path.Combine(fixture.Root, "file-instead-of-directory");
        File.WriteAllText(blocked, "fixture");
        string output = fixture.Build(cache: blocked);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public void DependencyPathsPreserveEscapedSpacesHashesDollarsAndDriveLetters()
    {
        const string text = "runtime: C:/sdk\\ space/a.h \\\r\n /tmp/value\\#1.h /tmp/cash$$.h C:\\sdk\\include\\b.h\n";
        Assert.Equal(new[] { "C:/sdk space/a.h", "/tmp/value#1.h", "/tmp/cash$.h", "C:\\sdk\\include\\b.h" },
            NativeRuntimeCache.Dependencies(text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshCompilerProcessesReuseOnePublishedRuntime(bool concurrent)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Source, """
            extern int __gflat_stdio_is_redirected(int selector) nothrow;
            int main() nothrow {
                if (__gflat_stdio_is_redirected(1) < 0) return 1;
                return 0;
            }
            """);
        async Task<int> Compile(string name, string? includes = null)
        {
            string traces = Path.Combine(fixture.Root, name + "-traces");
            string executable = Path.Combine(fixture.Root, name + ".exe");
            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = fixture.Root,
            };
            foreach (string argument in new[] { typeof(Program).Assembly.Location, fixture.Source,
                "--no-stdlib", "--no-builtins", "-o", executable }) start.ArgumentList.Add(argument);
            start.Environment["GFLAT_RUNTIME_CACHE_DIRECTORY"] = fixture.Cache;
            start.Environment["GFLAT_PROFILE_DIRECTORY"] = traces;
            if (includes != null) start.Environment["CPATH"] = includes;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Native runtime cache probe timed out");
            }
            Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
            Assert.Equal(0, NativeToolchain.Run(executable, []).ExitCode);
            return Directory.GetFiles(traces, "*.jsonl").SelectMany(File.ReadLines)
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .Count(item => item.GetProperty("stage").GetString() == "native.runtime-build");
        }
        if (concurrent)
        {
            int[] builds = await Task.WhenAll(Compile("first"), Compile("second"));
            Assert.Equal(1, builds.Sum());
        }
        else
        {
            Assert.Equal(1, await Compile("first"));
            Assert.Equal(0, await Compile("second"));
            string includeDirectory = Path.Combine(fixture.Root, "other includes");
            Directory.CreateDirectory(includeDirectory);
            Assert.Equal(1, await Compile("changed-environment", includeDirectory));
            Assert.Equal(0, await Compile("repeated-environment", includeDirectory));
        }
        Assert.Equal(concurrent ? 1 : 2, Directory.GetFiles(fixture.Cache, "current.json", SearchOption.AllDirectories).Length);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "gflat runtime cache " + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "runtime.c");
        public string Header => Path.Combine(Root, "value.h");
        public string Cache => Path.Combine(Root, "cache");
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Header, "#define VALUE 7\n");
            File.WriteAllText(Source, "#include \"value.h\"\nint cached_value(void) { return VALUE; }\n");
        }
        public string Build(string? option = null, string? cache = null)
        {
            var arguments = NativeRuntime.ObjectArguments(Source, NativeRuntimeCache.OutputPlaceholder, false,
                option == null ? [] : [option], []);
            return NativeRuntimeCache.GetObject(NativeToolchain.FindClang(), Source, "fixture", arguments, cache ?? Cache);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
