using System.Text.RegularExpressions;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class InlineTests
{
    [Fact]
    public void HintsSurviveGenericsPropertiesConstructorsAndOperators()
    {
        string ir = CompilerTestHelper.EmitIr("""
            inline int Identity<T>(T value) => (int)value;
            struct Number {
                public int Value;
                public inline Number(int value) { Value = value; }
                public inline int Read() => Value;
                public inline int P { get => Value; set { Value = value; } }
                public static inline Number operator +(Number a, Number b) => new Number(a.Value + b.Value);
            }
            struct Resource { public inline Resource() {} inline ~Resource() {} }
            class Box {
                public inline int Read() => 1;
                public inline int P { get; set; }
            }
            inline int Free() => 3;
            int main() {
                Number a = new Number(2); Number b = new Number(4);
                Number c = a + b;
                Resource resource = new Resource();
                Box box = new Box(); box.P = 1;
                c.P = 3;
                return Identity<int>(Free()) + c.Read() + box.Read() + box.P;
            }
            """);
        foreach (string name in new[] { "Identity", "$ctor$", "$dtor", "$op_Addition$", "$Read", "$get$P", "$set$P", "$Free" })
            Assert.True(ir.Split('\n').Any(line => line.StartsWith("define ") && line.Contains(name) && line.Contains("inlinehint")), name + "\n" + string.Join('\n', ir.Split('\n').Where(l => l.StartsWith("define "))));
    }

    [Theory]
    [InlineData("inline int value;", "callable")]
    [InlineData("inline struct S {}", "callable")]
    [InlineData("inline extern int F();", "extern")]
    [InlineData("interface I { inline int F(); }", "concrete")]
    [InlineData("interface I { inline int P { get; } }", "concrete")]
    [InlineData("abstract class C { public inline abstract int F(); }", "concrete")]
    [InlineData("inline inline int F() => 1;", "Duplicate")]
    public void InvalidHintsHaveDiagnostics(string source, string expected)
        => Assert.Contains(expected, Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source)).Message);

    [Fact]
    public void ClangOptimizesAnInlineCallAndAcceptsTheGeneratedAttributes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-inline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.ll"), output = Path.Combine(directory, "optimized.ll");
            File.WriteAllText(input, CompilerTestHelper.EmitIr("inline int Add(int a, int b) => a + b; int main() => Add(20, 22);"));
            var result = NativeToolchain.Run(NativeToolchain.FindClang(), ["-x", "ir", "-S", "-emit-llvm", "-O2", input, "-o", output]);
            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotMatch(new Regex("""(?:call|invoke) i32 @"?gflat\$Add"""), File.ReadAllText(output));
        }
        finally { Directory.Delete(directory, true); }
    }
}
