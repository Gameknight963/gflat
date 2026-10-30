using System.Security;
using gflat.Projects;

namespace gflat.Tests.Platform;

public sealed class WindowsSystemRandomTests
{
    private const string NativeDeclaration = "private extern int BCryptGenRandom(void*? algorithm, byte* output, uint count, uint flags) nothrow;";
    private const string NativeStub = """
        private int BCryptGenRandom(void*? algorithm, byte* output, uint count, uint flags) nothrow
        {
            Probe::Calls++;
            if (algorithm != null || flags != 2u || count != Probe::Expected || (output).reinterpret<nuint>() != Probe::Address)
                Probe::BadArguments = true;
            if (Probe::Write) {
                if (Probe::Status != 0) { if (count != 0u) output[0] = (byte)17; }
                else { for (uint i = 0u; i < count; i++) output[i] = (byte)(i + 3u); }
            }
            return Probe::Status;
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeterministicStubChecksPreflightArgumentsStatusAndPartialFailure(int optimization)
    {
        string original = File.ReadAllText(CopiedSource);
        Assert.Equal(1, original.Split(NativeDeclaration, StringSplitOptions.None).Length - 1);
        string fixture = original.Replace(NativeDeclaration, NativeStub, StringComparison.Ordinal);
        Assert.Equal(original, fixture.Replace(NativeStub, NativeDeclaration, StringComparison.Ordinal));
        const string program = """
            public static class Probe {
                public static int Calls;
                public static uint Expected;
                public static nuint Address;
                public static int Status;
                public static bool Write;
                public static bool BadArguments;
            }
            int main() nothrow {
                if (!Windows::SystemRandom::TryFill(((nuint)1).reinterpret<byte*>(), 0) || Probe::Calls != 0) return 1;
                if (sizeof(nint) != 8) {
                    if (Windows::SystemRandom::TryFill(((nuint)1).reinterpret<byte*>(), 1) || Probe::Calls != 0) return 2;
                    return 0;
                }
                if (Windows::SystemRandom::TryFill(((nuint)1).reinterpret<byte*>(), (nuint)4294967296ul) || Probe::Calls != 0) return 3;
                Probe::Expected = 4294967295u; Probe::Address = 1;
                if (!Windows::SystemRandom::TryFill(((nuint)1).reinterpret<byte*>(), (nuint)4294967295ul) || Probe::Calls != 1) return 4;
                if (Probe::BadArguments) return 5;
                byte[8] storage; for (nuint i = 0; i < 8; i++) storage[i] = (byte)165;
                Probe::Expected = 6u; Probe::Address = (&storage[1]).reinterpret<nuint>(); Probe::Write = true;
                if (!Windows::SystemRandom::TryFill(&storage[1], 6) || Probe::Calls != 2) return 6;
                for (nuint i = 0; i < 6; i++) if (storage[i + 1] != (byte)(i + 3)) return 7;
                if (storage[0] != (byte)165 || storage[7] != (byte)165 || Probe::BadArguments) return 8;
                Probe::Status = -1;
                if (Windows::SystemRandom::TryFill(&storage[1], 6) || Probe::Calls != 3) return 9;
                if (storage[1] != (byte)17 || storage[2] != (byte)4 || storage[6] != (byte)8) return 10;
                if (storage[0] != (byte)165 || storage[7] != (byte)165 || Probe::BadArguments) return 11;
                Probe::Status = 1; Probe::Write = false;
                if (Windows::SystemRandom::TryFill(&storage[1], 6) || Probe::Calls != 4) return 12;
                if (Windows::SystemRandom::TryFill(&storage[1], (nuint)4294967296ul) || Probe::Calls != 4) return 13;
                if (storage[1] != (byte)17 || storage[7] != (byte)165) return 14;
                return 0;
            }
            """;
        string ir = Emit([new(CopiedSource, fixture), new("windows-random-stub.gf", program)]);
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.True(result.ExitCode == 0, $"SystemRandom stub exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public async Task OptionalProjectPropagatesBcryptWhileClockProjectStaysIndependent()
    {
        var random = ProjectGraph.Load(RandomProject);
        var source = Assert.Single(random.Sources);
        Assert.Equal("SystemRandom.gf", Path.GetFileName(source));
        var requirement = Assert.Single(random.NativeLinkInputs.Libraries);
        Assert.Equal("Name", requirement.Kind);
        Assert.Equal("bcrypt", requirement.Value);
        var clock = ProjectGraph.Load(Path.Combine(Repository, "libraries", "platform", "windows", "Windows.gfproj"));
        Assert.Empty(clock.NativeLinkInputs.Libraries);
        Assert.DoesNotContain(clock.Sources, path => path.StartsWith(Path.GetDirectoryName(RandomProject)! + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
        string ir = Emit([new(source, File.ReadAllText(source)), new("windows-random-binding.gf",
            "int main() nothrow { byte[1] output; if (!Windows::SystemRandom::TryFill(output, 1)) return 1; return 0; }")]);
        Assert.Contains("@BCryptGenRandom(", ir);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(CopiedSource));

        string directory = Path.Combine(Path.GetTempPath(), "gflat-random-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string project = Path.Combine(directory, "app.gfproj");
            string reference = SecurityElement.Escape(ProjectSdk.EscapePropertyValue(RandomProject))!;
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content:
                "<ItemGroup><ProjectReference Include=\"" + reference + "\" /></ItemGroup>"));
            File.WriteAllText(Path.Combine(directory, "main.gf"), "int main() => 0;");
            var app = ProjectGraph.Load(project);
            Assert.Equal("bcrypt", Assert.Single(app.NativeLinkInputs.Libraries).Value);
            Assert.Contains(app.Sources, path => Path.GetFullPath(path) == Path.GetFullPath(source));
        }
        catch (Exception error)
        {
            error.Data["WindowsRandomGraphDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UnmodifiedWindowsBindingFillsGuardedBuffersWithExplicitNativeLinkage(int optimization)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(8, IntPtr.Size);
        var graph = ProjectGraph.Load(RandomProject);
        const string program = """
            int main() nothrow {
                byte[4098] storage;
                nuint[7] sizes = [0, 1, 31, 32, 255, 256, 4096];
                for (nuint sample = 0; sample < 7; sample++) {
                    for (nuint i = 0; i < 4098; i++) storage[i] = (byte)165;
                    nuint count = sizes[sample];
                    if (!Windows::SystemRandom::TryFill(&storage[1], count)) return 1;
                    if (storage[0] != (byte)165) return 2;
                    for (nuint i = count + 1; i < 4098; i++) if (storage[i] != (byte)165) return 3;
                }
                if (!Windows::SystemRandom::TryFill(((nuint)1).reinterpret<byte*>(), 0)) return 4;
                if (Windows::SystemRandom::TryFill(((nuint)1).reinterpret<byte*>(), (nuint)4294967296ul)) return 5;
                return 0;
            }
            """;
        var sources = graph.Sources.Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("windows-random-smoke.gf", program));
        string ir = Emit(sources);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-windows-random-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "program.ll"), output = Path.Combine(directory, "program.exe");
            File.WriteAllText(path, ir);
            var build = NativeToolchain.Compile(path, output, optimizationLevel: optimization, nativeLibraries: graph.NativeLinkInputs);
            Assert.True(build.ExitCode == 0, build.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Windows CNG exit {result.ExitCode}: {result.StandardError}");
        }
        catch (Exception error)
        {
            error.Data["WindowsSystemRandomNativeDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static string Repository => TestSupport.ProjectFileFixture.RepositoryRoot;
    private static string RandomProject => Path.Combine(Repository, "libraries", "platform", "windows-random", "WindowsRandom.gfproj");
    private static string CopiedSource => Path.Combine(AppContext.BaseDirectory, "Platform", "windows-random", "SystemRandom.gf");

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
