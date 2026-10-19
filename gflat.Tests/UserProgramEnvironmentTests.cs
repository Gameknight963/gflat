using System.Diagnostics;
using gflat.Projects;

namespace gflat.Tests;

public sealed class UserProgramEnvironmentTests
{
    private static readonly string[] PrivateBuildVariables = ["MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath"];

    private const string Source = """
        extern readonly(char)*? getenv(readonly(char)* name) nothrow;
        extern int puts(readonly(char)* text) nothrow;
        void Show(readonly(char)* name) nothrow {
            puts(name);
            readonly(char)*? value = getenv(name);
            if (value == null) puts("<absent>"); else puts(value!);
        }
        int main() nothrow {
            puts("BEGIN_USER_ENVIRONMENT");
            Show("MSBUILD_EXE_PATH"); Show("MSBuildExtensionsPath"); Show("MSBuildSDKsPath");
            Show("GFLAT_TEST_USER_ENVIRONMENT");
            puts("END_USER_ENVIRONMENT");
            return 0;
        }
        """;

    private static string Invoke(string[] arguments, bool callerValues)
    {
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        foreach (string name in PrivateBuildVariables)
        {
            if (callerValues) start.Environment[name] = "caller-original/" + name;
            else start.Environment.Remove(name);
        }
        start.Environment["GFLAT_TEST_USER_ENVIRONMENT"] = "unrelated-preserved";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(90000)) { process.Kill(true); throw new TimeoutException("User environment test timed out"); }
        string text = output.GetAwaiter().GetResult().Replace("\r\n", "\n");
        Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}: {errors.GetAwaiter().GetResult()}\n{text}");
        string expected = "BEGIN_USER_ENVIRONMENT\n" + string.Concat(PrivateBuildVariables.Select(name => name + "\n" + (callerValues ? "caller-original/" + name : "<absent>") + "\n"))
            + "GFLAT_TEST_USER_ENVIRONMENT\nunrelated-preserved\nEND_USER_ENVIRONMENT\n";
        Assert.Contains(expected, text);
        return text;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawSourceAndBuiltAndCachedProjectRunsPreserveCallerEnvironment(bool callerValues)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-user-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "main.gf"), project = Path.Combine(directory, "app.gfproj");
            File.WriteAllText(source, Source);
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: "<PropertyGroup><DebugSymbols>false</DebugSymbols></PropertyGroup>"));
            Invoke([source, "--no-stdlib", "-o", Path.Combine(directory, "source.exe"), "--run"], callerValues);
            Invoke(["build", project, "--run"], callerValues);
            string output = Path.Combine(directory, "bin/Debug/app.exe");
            string marker = Path.Combine(directory, "obj/Debug/app.build.success");
            DateTime binaryTime = File.GetLastWriteTimeUtc(output), successTime = File.GetLastWriteTimeUtc(marker);
            Invoke(["build", project, "--run"], callerValues);
            Assert.Equal(binaryTime, File.GetLastWriteTimeUtc(output));
            Assert.Equal(successTime, File.GetLastWriteTimeUtc(marker));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExplicitChildOverridesAndUnrelatedEnvironmentEntriesArePreserved()
    {
        var environment = new Dictionary<string, string?> {
            ["MSBUILD_EXE_PATH"] = "explicit-engine", ["MSBuildExtensionsPath"] = "explicit-extensions",
            ["MSBuildSDKsPath"] = "explicit-sdks", ["custom"] = "unchanged"
        };
        MsBuildHost.RestoreUserProcessEnvironment(environment);
        Assert.Equal("explicit-engine", environment["MSBUILD_EXE_PATH"]);
        Assert.Equal("explicit-extensions", environment["MSBuildExtensionsPath"]);
        Assert.Equal("explicit-sdks", environment["MSBuildSDKsPath"]);
        Assert.Equal("unchanged", environment["custom"]);
        environment.Remove("MSBuildSDKsPath");
        MsBuildHost.RestoreUserProcessEnvironment(environment);
        Assert.False(environment.ContainsKey("MSBuildSDKsPath"));
    }
}
