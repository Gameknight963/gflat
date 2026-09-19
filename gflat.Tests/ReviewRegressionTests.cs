using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class ReviewRegressionTests
{
    [Theory]
    [InlineData("int main() { int x; int* p = &x; return x; }")]
    [InlineData("int main() { int x; defer x = 1; return x; }")]
    [InlineData("int main() { int x; bool b = false && ((x = 1) == 1); return x; }")]
    [InlineData("int main() { int x; bool b = true || ((x = 1) == 1); return x; }")]
    [InlineData("int main() { int x; int[1] a; foreach (int v in a) { x = 1; } return x; }")]
    [InlineData("int main() { int x; defer { int y = x; } return 0; }")]
    public void RejectUnassignedReads(string source) => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));

    [Fact]
    public void DeferReadsAtScopeExit() => Compiler.Check("int main() { int x; defer { int y = x; } x = 7; return 0; }");

    [Theory]
    [InlineData("1 + 2")]
    [InlineData("(1 + 2)")]
    [InlineData("3")]
    public void ConstantArrayLengthsCompose(string length)
    {
        string ir = Compiler.Emit($"int main() {{ int[{length}] a; return sizeof(a); }}");
        Assert.Contains("[3 x i32]", ir);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("4294967297L")]
    public void InvalidArraySizeIsDiagnostic(string length) =>
        Assert.Throws<TypeCheckException>(() => Compiler.Check($"int main() {{ int[{length}] a; return 0; }}"));

    [Fact]
    public void NestedGenericClosersCompose() =>
        Compiler.Check("struct Box<T> { T value; } int main() { Box<Box<int>> b; return 0; }");

    [Fact]
    public void UnterminatedCommentIsDiagnostic() => Assert.Throws<TypeCheckException>(() => Lexer.Tokenize("/* unfinished"));

    [Fact]
    public void CommentPreservesLineNumbers() => Assert.Equal(3, Lexer.Tokenize("/* a\nb */\nint")[0].Line);

    [Fact]
    public void RejectConstantNullPointerCast() =>
        Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { int* p = (int*)0; return 0; }"));

    [Fact]
    public void CheckedInterfaceCastUsesInstancePointer()
    {
        string source = "interface I { int Value(); } struct S : I { public int Value() { return 7; } } int main() { S s = new S(); I* p = (I*)&s; return p.Value(); }";
        Assert.Equal(7, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("byte", 256, 0)]
    [InlineData("sbyte", 255, -1)]
    [InlineData("ushort", 65536, 0)]
    [InlineData("short", 65535, -1)]
    [InlineData("int", 4294967295, -1)]
    [InlineData("nint", 42, 42)]
    [InlineData("nuint", 42, 42)]
    public void ConstantCastMatchesRuntime(string type, long value, long expected)
    {
        var evaluator = new ConstEvaluator(new TypeChecker());
        var cast = new CastExpression(new NamedTypeExpression(type, null, 1),
            new LiteralExpression(new Token(TokenKind.LongLiteral, 1, 0, 1, value + "L"), 1), 1);
        Assert.True(evaluator.TryEvaluate(cast, out var result, out var reason), reason);
        long actual = result is ConstValue.UInteger u ? (long)u.Value : Assert.IsType<ConstValue.Integer>(result).Value;
        Assert.Equal(expected, actual);
        var execution = CompilerTestHelper.Run($"int main() {{ long x = {value}L; if ((long)({type})x == {expected}L) return 0; return 1; }}");
        Assert.Equal(0, execution.ExitCode);
    }

    [Fact]
    public void AllocationFailureIsGuarded()
    {
        string ir = Compiler.Emit("struct S { int x; } int main() { S* p = new* S(); delete p; return 0; }");
        Assert.Contains("null_failure", ir);
        Assert.Contains("call void @llvm.trap()", ir);
    }

    [Fact]
    public void UnsupportedTargetIsRejected() => Assert.Throws<ArgumentException>(() => TargetInfo.Parse("i386-pc-windows-msvc"));

    [Theory]
    [InlineData("int main() { int x; while (true) { x = 7; break; } return x; }", 7)]
    [InlineData("int main() { int x = 0; bool b = false && ((x = 1) == 1); return x; }", 0)]
    [InlineData("int main() { int x = 0; bool b = true || ((x = 1) == 1); return x; }", 0)]
    [InlineData("int main() { int x = 1; { int x = 2; } return x; }", 1)]
    [InlineData("int main() { int x; { defer x = 9; } return x; }", 9)]
    public void ControlFlowMatchesExecution(string source, int expected) =>
        Assert.Equal(expected, CompilerTestHelper.Run(source).ExitCode);

    [Fact]
    public void DeferKeepsLexicalBindings()
    {
        string source = "extern int printf(readonly char* f, ...); int main() { int x = 1; defer printf(\"%d\", x); { int x = 2; return 0; } }";
        Assert.Equal("1", CompilerTestHelper.Run(source).StandardOutput);
    }

    [Theory]
    [InlineData("int main() { uint x = 1u; int y = x; return y; }")]
    [InlineData("int main() { ulong x = 1ul; long y = x; return 0; }")]
    [InlineData("struct S { int x; ~S() {} } S f(S* p) { return *p; } int main() { return 0; }")]
    public void RejectLossyOrBorrowedTransfers(string source) => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));

    [Fact]
    public void InfiniteLoopIgnoresUnreachableBreak() => Compiler.Check("int f() { while (true) { if (false) break; } } int main() { return 0; }");

    [Fact]
    public void LogicalConditionCarriesAssignmentIntoTrueBranch() =>
        Compiler.Check("int f(bool b) { int x; if (b && ((x = 3) == 3)) return x; return 0; } int main() { return f(true); }");

    [Fact]
    public void FailingAllocatorTrapsBeforeConstruction()
    {
        var result = CompilerTestHelper.Run("void*? __gflat_alloc(ulong size) { return null; } void __gflat_free(void* p) {} struct S { int x; } int main() { S* p = new* S(); return 0; }");
        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("const int f() { int x = 2147483647; return x + 1; }")]
    [InlineData("const int f() { int x = 5; x += 7; return x; }")]
    public void ConstArithmeticMatchesRuntime(string function)
    {
        var result = CompilerTestHelper.Run(function + " int main() { const int folded = f(); int actual = f(); if (actual == folded) return 0; return 1; }");
        Assert.Equal(0, result.ExitCode);
    }
}
