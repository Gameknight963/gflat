namespace gflat.Tests;

public sealed class RuntimeDependencyTests
{
    [Fact]
    public void DebugFilenameMarkersDoNotSelectEitherNativeRuntime()
    {
        string path = Path.GetFullPath("@__gflat_eh_@__gflat_stdio_.gf");
        string ir = Compiler.Emit([new SourceFile(path, "int main()=>0;")], options: new()
            { NoStdLib = true, DebugSymbols = true, InternalizeFunctions = true });
        Assert.Contains("!DIFile(filename: \"@__gflat_eh_@__gflat_stdio_.gf\"", ir);
        // These assertions establish that the old raw-substring checks really match.
        Assert.Contains("@__gflat_eh_", ir);
        Assert.Contains("@__gflat_stdio_", ir);
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
    }

    [Fact]
    public void ActualEmittedRuntimeDeclarationsStillSelectOnlyTheirOwnRuntime()
    {
        string exceptionIr = Compiler.Emit("""
            void Release()=>__llvm<void>("call void @__gflat_eh_release(i8* null)\nret void");
            int main()=>0;
            """, options: new() { NoStdLib = true });
        Assert.Contains("declare void @__gflat_eh_release(i8*) nounwind", exceptionIr);
        Assert.Equal((true, false), NativeToolchain.RequiredNativeRuntimes(exceptionIr));
        string stdioIr = Compiler.Emit("""
            extern void* __gflat_stdio_stdin() nothrow;
            int main() nothrow {__gflat_stdio_stdin();return 0;}
            """, options: new() { NoStdLib = true });
        Assert.Contains("@__gflat_stdio_stdin(", stdioIr);
        Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(stdioIr));
    }

    [Fact]
    public void HeaderClassificationHandlesQuotedMultilineNamesWithoutReadingCommentsOrStringsAsSymbols()
    {
        foreach (string ir in new[] {
            "; declare void @__gflat_eh_release()\n; @__gflat_stdio_stdin\n",
            "!0 = !{!\"@__gflat_eh_release @__gflat_stdio_stdin\"}\n",
            "declare void @\"ordinary@__gflat_eh_@__gflat_stdio_\"()\n",
            "@\"__gflat_eh_data\" = private constant [1 x i8] zeroinitializer\n"})
        {
            Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        }
        string quotedException = "declare\n void\n @\"__gflat_eh_release\"\n (i8*) nounwind\n";
        Assert.Equal((true, false), NativeToolchain.RequiredNativeRuntimes(quotedException));
        string quotedStdio = "define ptr\n @\"__gflat_stdio_stdin\"() { ret ptr null }\n";
        Assert.Equal((false, true), NativeToolchain.RequiredNativeRuntimes(quotedStdio));
        Assert.Equal((true, true), NativeToolchain.RequiredNativeRuntimes(quotedException + quotedStdio));
    }
}
