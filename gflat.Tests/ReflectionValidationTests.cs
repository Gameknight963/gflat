using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionValidationTests
{
    [Theory]
    [InlineData("reflection*")]
    [InlineData("reflection**")]
    [InlineData("reflection[2]*")]
    public void RuntimePointersCannotAddressCompilerOnlyValues(string type)
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("int main(){ " + type + " storage; return 0; }"));
        Assert.Contains("no runtime address", error.Message);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void InstanceMethodsWithReflectionSignaturesMustBeConstant(string kind)
    {
        var error = Assert.Throws<TypeCheckException>(() => Check(kind + " Item { public reflection Read() => reflect(int); } int main() => 0;"));
        Assert.Contains("must be const", error.Message);
    }

    [Fact]
    public void MethodDeclarationsExposeAccessAndReceiverContracts()
    {
        Check("""
            struct Item {
                private int Hidden() => 0;
                public readonly int Read() nothrow => 42;
            }
            int main() {
                const reflection read = reflect(Item::Read);
                static if (!read.IsPublic || !read.IsReadOnly || !read.IsNoThrow || read.IsStatic) { invalidRead(); }
                static if (!reflect(Item::Hidden).IsPrivate) { invalidPrivate(); }
                return 0;
            }
            """);
    }

    [Fact]
    public void UnknownFlagIsRejectedEvenInsideAnUnusedConstantFunction()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            const bool Check(reflection info) { return info.IsStrcut; }
            int main() => 0;
            """));
        Assert.Contains("IsStrcut", error.Message);
    }

    [Fact]
    public void CompileTimeOnlyCallCannotLeaveRuntimeArgumentsForTheBackend()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            const int Choose(reflection info, int number) { return number; }
            int Read() => 42;
            int main() { return Choose(reflect(int), Read()); }
            """));
        Assert.Contains("compile-time", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TypeSplicesCanQualifyStaticMembersAndNamesKeepGenericArguments(int optimization)
    {
        string ir = Compiler.Emit("""
            namespace Models {
                struct Item { }
                struct Box<T> { public static int Read() => 42; }
            }
            int main() {
                const reflection item = reflect(Models::Box<Models::Item>);
                const readonly(char)* name = item.Name;
                if (name[0] != 'B' || name[1] != 'o' || name[2] != 'x' || name[3] != '<') return 1;
                return [:item:]::Read() - 42;
            }
            """, options: new() { NoStdLib = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static void Check(string code) => Compiler.Check(code, options: new() { NoStdLib = true });
}
