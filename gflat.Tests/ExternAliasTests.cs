namespace gflat.Tests;

public class ExternAliasTests
{
    [Fact]
    public void NamespacedExternReturnAliasesUseTheirNativeRepresentation()
    {
        string ir = CompilerTestHelper.EmitIr("""
            namespace Native {
                alias FileHandle = void*?;
                private extern FileHandle fopen(readonly(char*) path, readonly(char*) mode);
                private extern int fclose(FileHandle handle);
                void Probe() { FileHandle handle = fopen("missing-file", "rb"); if (handle != null) fclose(handle); }
            }
            int main() => 0;
            """);
        Assert.Contains("declare i8* @fopen(i8*, i8*)", ir);
        Assert.DoesNotContain("%FileHandle", ir);
        Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Native { alias Number = int; extern Number abs(Number value); Number Get() => abs(-42); }
            int main() => Native::Get() - 42;
            """).ExitCode);
    }
}
