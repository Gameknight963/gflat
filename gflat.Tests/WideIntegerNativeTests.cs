namespace gflat.Tests;

public sealed class WideIntegerNativeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RuntimeWideDivisionAndRemainderLinkWithoutStdOrExceptionRuntime(int optimization)
    {
        string ir = Compiler.Emit("""
            int main(int argc, char** argv) {
                extralong value = (extralong)argc << 96;
                extralong divisor = (extralong)(argc + 2);
                if (value % divisor != (extralong)1) return 1;
                if (-value % divisor != (extralong)-1) return 2;
                if (value / divisor * divisor + value % divisor != value) return 3;
                return 42;
            }
            """, options: new() { NoStdLib = true });
        Assert.Contains("srem i128", ir);
        Assert.Contains("sdiv i128", ir);
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        string root = Path.Combine(Path.GetTempPath(), "gflat_wide_native_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "wide.ll"), executable = Path.Combine(root, "wide.exe");
            File.WriteAllText(source, ir);
            var compilation = NativeToolchain.Compile(source, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(root, true); }
    }
}
