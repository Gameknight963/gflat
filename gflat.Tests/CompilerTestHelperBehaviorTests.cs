namespace gflat.Tests;

[Collection("Project SDK environment")]
public sealed class CompilerTestHelperBehaviorTests
{
    [Fact]
    public async Task MalformedIrRetainsItsFixtureAndOriginalCompilerFailure()
    {
        const string ir = "this is not LLVM IR\n";
        Exception error = Assert.Throws<Exception>(() => CompilerTestHelper.RunIr(ir));
        string directory = Assert.IsType<string>(error.Data["CompilerTestHelperDirectory"]);
        try
        {
            Assert.StartsWith("clang compilation failed: ", error.Message);
            Assert.Contains("IR:\n" + ir, error.Message);
            Assert.Contains("CompilerTestHelper.RunIr", error.StackTrace);
            Assert.True(Directory.Exists(directory));
            Assert.Equal(ir, File.ReadAllText(Path.Combine(directory, "test.ll")));
        }
        catch (Exception assertionError)
        {
            assertionError.Data["CompilerTestHelperDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void CompletedProgramsReturnTheirStatusAndCleanTheirFixture(int exitCode)
    {
        string ir = $$"""
            declare i32 @puts(ptr)
            define i32 @main(i32 %argc, ptr %argv) {
            entry:
                %path = load ptr, ptr %argv
                %ignored = call i32 @puts(ptr %path)
                ret i32 {{exitCode}}
            }
            """;
        ExecutionResult result = CompilerTestHelper.RunIr(ir);
        string executable = result.StandardOutput.Trim();
        string? directory = Path.GetDirectoryName(executable);
        try
        {
            Assert.Equal(exitCode, result.ExitCode);
            Assert.Empty(result.StandardError);
            Assert.True(Path.IsPathFullyQualified(executable), $"Expected argv[0] path, received '{executable}'.");
            Assert.Equal("test.exe", Path.GetFileName(executable));
            Assert.NotNull(directory);
            Assert.False(Directory.Exists(directory), $"Completed fixture was retained: {directory}");
        }
        catch (Exception error)
        {
            error.Data["CompilerTestHelperDirectory"] = directory;
            throw;
        }
    }
}
