using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ContextualConstantTests
{
    [Theory]
    [InlineData("const extralong Value = 18446744073709551616; int main() { if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("int main() { const extralong Value = 18446744073709551616; if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("class Numbers { public const extralong Value = 18446744073709551616; } int main() { if ((Numbers::Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("struct Numbers { public const extralong Value = 18446744073709551616; } int main() { if ((Numbers::Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("extralong Read(extralong value = 18446744073709551616) => value; int main() { if ((Read() >> 64) == 1) return 0; return 1; }")]
    [InlineData("extralong Read(extralong value = Numbers::Value) => value; struct Numbers { public const extralong Value = 18446744073709551616; } int main() { if ((Read() >> 64) == 1) return 0; return 1; }")]
    [InlineData("const extralong Make() => 18446744073709551616; const extralong Value = Make(); int main() { if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("alias Wide = extralong; const Wide Value = 18446744073709551616; int main() { if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("const extralong Value = 0x10000000000000000; int main() { if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("const extralong Value = -170141183460469231731687303715884105728; int main() { if ((Value >> 127) == (extralong)-1) return 0; return 1; }")]
    [InlineData("const extralong Make() => -170141183460469231731687303715884105728; const extralong Value = Make(); int main() { if ((Value >> 127) == (extralong)-1) return 0; return 1; }")]
    [InlineData("extralong Value = 18446744073709551616; int main() { if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("class Numbers { public static extralong Value = 18446744073709551616; } int main() { if ((Numbers::Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("const int Narrow() => 2; const extralong Make() { int count = Narrow(); if (count == 2) return 18446744073709551616; return 0; } const extralong Value = Make(); int main() { if ((Value >> 64) == 1) return 0; return 1; }")]
    [InlineData("const extralong Make() { extralong value = 18446744073709551616; return value + (extralong)sizeof(int[1 << 2]); } const extralong Value = Make(); int main() { if ((Value >> 64) != 1 || (int)Value != 16) return 1; return 0; }")]
    [InlineData("const ulong Value = 18446744073709551615; int main() { if (Value == 18446744073709551615ul) return 0; return 1; }")]
    public void DeclaredNumericTargetsApplyToLiteralEvaluation(string source)
        => Assert.Equal(0, CompilerTestHelper.Run(source).ExitCode);

    [Theory]
    [InlineData("const extralong Value = 18446744073709551616l;")]
    [InlineData("const extralong Make() => 18446744073709551616l;")]
    [InlineData("extralong Read(extralong value = 18446744073709551616ul) => value;")]
    public void TargetedEvaluationDoesNotBypassSourceTyping(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source + " int main() => 0;"));
}
