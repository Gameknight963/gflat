namespace gflat.Tests;

public class VariadicFloatTests
{
    [Fact]
    public void FloatingArgumentsUseCVariadicCallingConvention()
    {
        const string source = """
            extern int printf(readonly(char)* format, ...);
            int main() { printf(c"%.2f %.2f %d", 1.5f, 2.25d, (byte)255); return 0; }
            """;
        string ir = CompilerTestHelper.EmitIr(source);
        Assert.Contains("call i32 (i8*, ...) @printf", ir);
        Assert.Contains("fpext float", ir);
        var result = CompilerTestHelper.Run(source);
        Assert.Equal("1.50 2.25 255", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }
}
