namespace gflat.Tests;

public sealed class NativeCatchWithoutStandardTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CatchAllRethrowsNativeExceptionsAndCleansUpWithoutAnExceptionDeclaration(int optimization)
    {
        string ir = Compiler.Emit("""
            extern "C" void NativeThrow();
            int main()
            {
                int cleaned = 0;
                try
                {
                    defer { cleaned++; }
                    try { NativeThrow(); }
                    catch { throw; }
                }
                catch
                {
                    if (cleaned == 1) return 42;
                    return 1;
                }
                return 2;
            }
            """, options: new() { NoStdLib = true });
        Assert.DoesNotContain("%Exception", ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-native-catch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "program.ll"), executable = Path.Combine(directory, "program.exe");
            File.WriteAllText(source, ir);
            var unit = new ForeignTranslationUnit("cpp", "extern \"C\" void NativeThrow() { throw 7; }",
                NativeRuntime.HostCompilerArguments(true));
            var compiled = NativeToolchain.Compile(source, executable, optimizationLevel: optimization, foreignUnits: [unit]);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
