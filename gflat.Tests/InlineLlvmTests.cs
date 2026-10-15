using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class InlineLlvmTests
{
    [Fact]
    public void BodyCallsAreTypedAndCanBeWrappedInOrdinaryGenericFunctions()
    {
        const string source = """
            int Add(int a, int b) => __llvm<int>("%sum = add i32 %0, %1\nret i32 %sum", a, b);
            T Identity<T>(T value) => __llvm<T>("ret i32 %0", value);
            int main() => Identity<int>(Add(20, 22));
            """;
        string ir = CompilerTestHelper.EmitIr(source);
        Assert.Contains("define private i32 @gflat$llvm$", ir);
        Assert.Contains("alwaysinline", ir);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void BodyCanStoreThroughExplicitRawPointer()
    {
        var result = CompilerTestHelper.Run("""
            void Store(int* target, int value) { __llvm<void>("store i32 %1, i32* %0\nret void", target, value); }
            int main() { int value = 0; Store(&value, 42); return value; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void LlvmInsideLambdaEmitsItsHelperAtModuleScope()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            int main() {
                int(int)* operation = (int value) => __llvm<int>("%result = add i32 %0, 1\nret i32 %result", value);
                return operation(41);
            }
            """).ExitCode);
    }

    [Fact]
    public void ModulesCanCallIntrinsicsAndReuseEntryNamesWithoutDuplicateDeclarations()
    {
        const string source = """
            int Count(int value) => __llvm_module<int>("declare i32 @llvm.ctpop.i32(i32)\ndefine i32 @count(i32 %value) {\n%result = call i32 @llvm.ctpop.i32(i32 %value)\nret i32 %result\n}", "count", value);
            int Again(int value) => __llvm_module<int>("declare i32 @llvm.ctpop.i32(i32)\ndefine i32 @count(i32 %value) {\n%result = call i32 @llvm.ctpop.i32(i32 %value)\nret i32 %result\n}", "count", value);
            int main() => Count(7) + Again(15);
            """;
        Assert.Equal(7, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ModuleRenamingPreservesStringDataAndComments()
    {
        string ir = CompilerTestHelper.EmitIr("""
            int main() => __llvm_module<int>("@text = private constant [6 x i8] c\"@entry\"\n; @entry stays in this comment\ndefine i32 @entry() { ret i32 42 }", "entry");
            """);
        Assert.Contains("c\"@entry\"", ir);
        Assert.Contains("; @entry stays in this comment", ir);
        Assert.DoesNotContain("define i32 @entry", ir);
    }

    [Theory]
    [InlineData("__llvm(\"ret i32 0\")")]
    [InlineData("__llvm<int, int>(\"ret i32 0\")")]
    [InlineData("__llvm<int>(12)")]
    [InlineData("__llvm_module<int>(\"define i32 @entry() { ret i32 0 }\", \"bad name\")")]
    [InlineData("__llvm_module<int>(\"define i32 @entry() { ret i32 0 }\", \"missing\")")]
    [InlineData("__llvm<int>(\"ret i32 0\", new Data())")]
    public void InvalidCallContractsProduceSourceDiagnostics(string expression)
    {
        Assert.Throws<TypeCheckException>(() => CompilerTestHelper.EmitIr("struct Data { public int value; } int main() => " + expression + ";"));
    }

    [Fact]
    public void ManagedValuesCannotBypassLifecycleThroughLlvm()
    {
        Assert.Throws<TypeCheckException>(() => CompilerTestHelper.EmitIr("int Read(int^ value) => __llvm<int>(\"ret i32 0\", value);"));
        Assert.Throws<TypeCheckException>(() => CompilerTestHelper.EmitIr("int^ Read() => __llvm<int^>(\"ret i32* null\");"));
    }
}
