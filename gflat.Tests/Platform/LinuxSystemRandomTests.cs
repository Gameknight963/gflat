using System.Security;
using gflat.Projects;

namespace gflat.Tests.Platform;

public sealed class LinuxSystemRandomTests
{
    private const string NativeDeclaration = "private extern int getentropy(void* output, nuint count) nothrow;";
    private const string NativeStub = """
        private int getentropy(void* output, nuint count) nothrow
        {
            Probe::Calls++;
            nuint expected = Probe::Length - Probe::Offset;
            if (expected > 256) expected = 256;
            if ((nuint)output != Probe::Address + Probe::Offset || count != expected || count == 0 || count > 256)
                { Probe::BadArguments = true; return -1; }
            byte* bytes = (byte*)output;
            if (Probe::Calls == Probe::FailAt) { bytes[0] = (byte)17; return -1; }
            for (nuint i = 0; i < count; i++) bytes[i] = (byte)((Probe::Offset + i) % 251);
            Probe::Offset += count;
            return 0;
        }
        """;
    private const string Probe = """
        public static class Probe {
            public static int Calls;
            public static int FailAt;
            public static nuint Address;
            public static nuint Length;
            public static nuint Offset;
            public static bool BadArguments;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeterministicStubChecksZeroChunkBoundariesAddressesAndGuards(int optimization)
        => RunStub("""
            int main() nothrow {
                if (!Linux::SystemRandom::TryFill((byte*)1, 0) || Probe::Calls != 0) return 1;
                byte[515] storage;
                nuint[6] sizes = [0, 1, 255, 256, 257, 513];
                int[6] calls = [0, 1, 1, 1, 2, 3];
                for (nuint sample = 0; sample < 6; sample++) {
                    for (nuint i = 0; i < 515; i++) storage[i] = (byte)165;
                    Probe::Calls = 0; Probe::FailAt = 0; Probe::Offset = 0; Probe::BadArguments = false;
                    Probe::Address = (nuint)&storage[1]; Probe::Length = sizes[sample];
                    if (!Linux::SystemRandom::TryFill(&storage[1], sizes[sample])) return 2;
                    if (Probe::Calls != calls[sample] || Probe::Offset != sizes[sample] || Probe::BadArguments) return 3;
                    for (nuint i = 0; i < sizes[sample]; i++) if (storage[i + 1] != (byte)(i % 251)) return 4;
                    if (storage[0] != (byte)165) return 5;
                    for (nuint i = sizes[sample] + 1; i < 515; i++) if (storage[i] != (byte)165) return 6;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeFailureStopsImmediatelyWithoutRollingBackEarlierBytes(int optimization)
        => RunStub("""
            int main() nothrow {
                byte[515] storage;
                for (int failAt = 1; failAt <= 2; failAt++) {
                    for (nuint i = 0; i < 515; i++) storage[i] = (byte)165;
                    Probe::Calls = 0; Probe::Offset = 0; Probe::FailAt = failAt; Probe::BadArguments = false;
                    Probe::Address = (nuint)&storage[1]; Probe::Length = 513;
                    if (Linux::SystemRandom::TryFill(&storage[1], 513)) return 1;
                    if (Probe::Calls != failAt || Probe::BadArguments) return 2;
                    nuint completed = 0;
                    if (failAt == 2) completed = 256;
                    if (Probe::Offset != completed) return 3;
                    for (nuint i = 0; i < completed; i++) if (storage[i + 1] != (byte)(i % 251)) return 4;
                    if (storage[completed + 1] != (byte)17) return 5;
                    for (nuint i = completed + 2; i < 515; i++) if (storage[i] != (byte)165) return 6;
                    if (storage[0] != (byte)165) return 7;
                }
                return 0;
            }
            """, optimization);

    [Fact]
    public async Task OptionalProjectAndCopiedBindingStayIndependentOfLinuxClocks()
    {
        var random = ProjectGraph.Load(RandomProject);
        string source = Assert.Single(random.Sources);
        Assert.Equal("SystemRandom.gf", Path.GetFileName(source));
        Assert.Empty(random.NativeLinkInputs.Libraries);
        var clock = ProjectGraph.Load(Path.Combine(Repository, "libraries", "platform", "linux", "Linux.gfproj"));
        Assert.Empty(clock.NativeLinkInputs.Libraries);
        Assert.DoesNotContain(clock.Sources, path => path.StartsWith(Path.GetDirectoryName(RandomProject)! + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
        string ir = Emit([new(source, File.ReadAllText(source)), new("linux-random-binding.gf",
            "int main() nothrow { byte[1] output; if (!Linux::SystemRandom::TryFill(output, 1)) return 1; return 0; }")]);
        Assert.Contains("@getentropy(", ir);
        Assert.DoesNotContain("@getrandom(", ir);
        Assert.DoesNotContain("@__errno_location(", ir);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(CopiedSource));

        string directory = Path.Combine(Path.GetTempPath(), "gflat-linux-random-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "app.gfproj");
            string reference = SecurityElement.Escape(ProjectSdk.EscapePropertyValue(RandomProject))!;
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content:
                "<ItemGroup><ProjectReference Include=\"" + reference + "\" /></ItemGroup>"));
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => 0;");
            var app = ProjectGraph.Load(project);
            Assert.Empty(app.NativeLinkInputs.Libraries);
            Assert.Equal(2, app.Sources.Count);
            Assert.Contains(app.Sources, path => Path.GetFullPath(path) == Path.GetFullPath(source));
        }
        catch (Exception error)
        {
            error.Data["LinuxRandomGraphDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UnmodifiedLinuxBindingFillsGuardedBuffersUsingDefaultLibc(int optimization)
    {
        // On other hosts, this is not native Linux execution or ABI validation.
        if (!OperatingSystem.IsLinux()) return;
        var graph = ProjectGraph.Load(RandomProject);
        const string program = """
            int main() nothrow {
                byte[4098] storage;
                nuint[7] sizes = [0, 1, 255, 256, 257, 513, 4096];
                for (nuint sample = 0; sample < 7; sample++) {
                    for (nuint i = 0; i < 4098; i++) storage[i] = (byte)165;
                    nuint count = sizes[sample];
                    if (!Linux::SystemRandom::TryFill(&storage[1], count)) return 1;
                    if (storage[0] != (byte)165) return 2;
                    for (nuint i = count + 1; i < 4098; i++) if (storage[i] != (byte)165) return 3;
                }
                if (!Linux::SystemRandom::TryFill((byte*)1, 0)) return 4;
                return 0;
            }
            """;
        var sources = graph.Sources.Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("linux-random-smoke.gf", program));
        string ir = Emit(sources);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-linux-random-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "program.ll"), output = Path.Combine(directory, "program");
            File.WriteAllText(path, ir);
            var build = NativeToolchain.Compile(path, output, optimizationLevel: optimization, nativeLibraries: graph.NativeLinkInputs);
            Assert.True(build.ExitCode == 0, build.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Linux getentropy exit {result.ExitCode}: {result.StandardError}");
        }
        catch (Exception error)
        {
            error.Data["LinuxSystemRandomNativeDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static void RunStub(string program, int optimization)
    {
        string original = File.ReadAllText(CopiedSource);
        Assert.Equal(1, original.Split(NativeDeclaration, StringSplitOptions.None).Length - 1);
        string fixture = original.Replace(NativeDeclaration, NativeStub, StringComparison.Ordinal);
        Assert.Equal(original, fixture.Replace(NativeStub, NativeDeclaration, StringComparison.Ordinal));
        string ir = Emit([new(CopiedSource, fixture), new("linux-random-stub.gf", Probe + program)]);
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.True(result.ExitCode == 0, $"Linux SystemRandom stub exit {result.ExitCode}: {result.StandardError}");
    }

    private static string Repository => TestSupport.ProjectFileFixture.RepositoryRoot;
    private static string RandomProject => Path.Combine(Repository, "libraries", "platform", "linux-random", "LinuxRandom.gfproj");
    private static string CopiedSource => Path.Combine(AppContext.BaseDirectory, "Platform", "linux-random", "SystemRandom.gf");

    private static string Emit(IEnumerable<SourceFile> sources)
    {
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        Assert.DoesNotContain("@malloc", ir);
        Assert.DoesNotContain("gflat$Memory$Allocate", ir);
        Assert.DoesNotContain("@__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        return ir;
    }
}
