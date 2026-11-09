using System.Text.Json;

namespace gflat.Tests;

[Collection("Project SDK environment")]
public sealed class NativeFailureArtifactTests
{
    [Fact]
    [Trait("Optimization", "Independent")]
    public async Task UnexpectedNativeExitRetainsTheExecutableAndItsDiagnostics()
    {
        string directory = Directory.CreateTempSubdirectory("gflat-exit-artifacts-").FullName;
        string? previous = Environment.GetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT");
        string? retained = null;
        try
        {
            Environment.SetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT", directory);
            var error = Assert.Throws<InvalidOperationException>(() => CompilerTestHelper.RunIr(
                "define i32 @main() { ret i32 7 }", optimizationLevel: 0, expectedExitCode: 0));
            retained = Assert.IsType<string>(error.Data["CompilerTestHelperDirectory"]);
            string artifact = Assert.IsType<string>(error.Data["CompilerTestHelperArtifactDirectory"]);
            Assert.True(new FileInfo(Path.Combine(artifact, "test.exe")).Length > 0);
            Assert.Contains("actual=7, expected=0", File.ReadAllText(Path.Combine(artifact, "failure.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT", previous);
            if (retained != null)
            {
                string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.StartsWith(temporaryRoot, Path.GetFullPath(retained), StringComparison.OrdinalIgnoreCase);
                await TestDirectoryCleanup.DeleteAsync(retained);
            }
            await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Optimization", "Independent")]
    public async Task FailedCompilationRetainsItsInputAndPreservesTheOriginalErrorWhenStorageFails(bool unavailable)
    {
        string directory = Directory.CreateTempSubdirectory("gflat-failure-artifacts-").FullName;
        string root = Path.Combine(directory, "reports");
        if (unavailable) File.WriteAllText(root, "occupied");
        string? previous = Environment.GetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT");
        string? retained = null;
        try
        {
            Environment.SetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT", root);
            const string invalidIr = "this is not LLVM IR";
            var error = Assert.Throws<Exception>(() => CompilerTestHelper.RunIr(invalidIr, optimizationLevel: 0));
            Assert.Contains("clang compilation failed", error.Message);
            retained = Assert.IsType<string>(error.Data["CompilerTestHelperDirectory"]);
            if (unavailable)
            {
                Assert.IsType<string>(error.Data["CompilerTestHelperArtifactError"]);
                Assert.False(error.Data.Contains("CompilerTestHelperArtifactDirectory"));
            }
            else
            {
                string artifact = Assert.IsType<string>(error.Data["CompilerTestHelperArtifactDirectory"]);
                Assert.Equal(root, Path.GetDirectoryName(artifact));
                Assert.Equal(invalidIr, File.ReadAllText(Path.Combine(artifact, "test.ll")));
                using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifact, "failure.json")));
                Assert.Equal(retained, metadata.RootElement.GetProperty("OriginalDirectory").GetString());
                Assert.Contains("clang compilation failed", metadata.RootElement.GetProperty("Exception").GetString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GFLAT_TEST_FAILURE_ARTIFACT_ROOT", previous);
            if (retained != null)
            {
                string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.StartsWith(temporaryRoot, Path.GetFullPath(retained), StringComparison.OrdinalIgnoreCase);
                await TestDirectoryCleanup.DeleteAsync(retained);
            }
            await TestDirectoryCleanup.DeleteAsync(directory);
        }
    }
}
