using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GlobalTypeLayoutQualificationTests
{
    [Theory]
    [InlineData("sizeof")]
    [InlineData("alignof")]
    public void QualifiedLayoutCannotFallBackToAnUnrelatedVariable(string operation)
    {
        (string Target, string Local)[] cases = [("global::A", "A"), ("A::Missing", "Missing")];
        foreach (var (target, local) in cases)
        foreach (bool constantFunction in new[] { false, true })
        {
            string code = constantFunction
                ? $"namespace A{{}} const int Compute(){{int {local}=1;return {operation}({target});}} const int Value=Compute();"
                : $"namespace A{{}} int Probe(){{int {local}=1;return {operation}({target});}}";
            var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(code,
                options: new() { NoStdLib = true, NoBuiltins = true }));
            Assert.Contains(local, error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("sizeof", 4)]
    [InlineData("alignof", 4)]
    public void UnqualifiedVariableLayoutAndActualRootTypeLayoutRemainValidConstants(string operation, int expected)
    {
        var (_, checker) = Compiler.Check($$"""
            namespace A{}
            const int Scalar=1;
            struct RootType{public byte Marker;}
            const int Variable={{operation}}(Scalar);
            const int Root={{operation}}(global::RootType);
            int Probe(){int A=1;return {{operation}}(A);}
            """, options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.True(checker.TryGetConstValueByName("Variable", out var variable));
        Assert.Equal(expected, Assert.IsType<ConstValue.Integer>(variable).Value);
        Assert.True(checker.TryGetConstValueByName("Root", out var root));
        Assert.Equal(1, Assert.IsType<ConstValue.Integer>(root).Value);
    }
}
