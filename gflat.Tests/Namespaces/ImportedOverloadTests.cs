using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class ImportedOverloadTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ImportedFunctionsSelectByArgumentsAndTargetSignaturesWithoutChangingQualifiedCalls(bool reverse, int optimization)
    {
        string imports = reverse ? "using Right; using Left; using Right;" : "using Left; using Right; using Left;";
        string left = """
            namespace Left {
                int Read(int value) => 11;
                int Choose(int value) => 31;
                int Apply<T>(T value) => 41;
                int Family<T>(T value) => 51;
                int Family<T>(T* value) => 53;
                const int Increment = 5;
                int WithDefault(int value, int increment = Increment) => value + increment;
                const int Evaluate(int value) => 101;
                T Identity<T>(T value) => value;
            }
            """;
        string right = """
            namespace Right {
                int Read(bool value) => 22;
                int Choose<T>(T value) => 37;
                int Apply<T, U>(T value, U other) => 43;
                int Family(bool value) => 59;
                const int Increment = 7;
                int WithDefault(bool value, int increment = Increment) => increment;
                const int Evaluate(bool value) => 103;
                T Identity<T>(T value) => value;
            }
            """;
        var sources = new List<SourceFile>();
        if (reverse) { sources.Add(new("right.gf", right)); sources.Add(new("left.gf", left)); }
        else { sources.Add(new("left.gf", left)); sources.Add(new("right.gf", right)); }
        sources.Add(new("main.gf", imports + """
            int Invoke(int(int)* callback) => callback(0);
            const int Increment = 1000;
            const int FoldedInteger = Evaluate(0);
            const int FoldedBoolean = Evaluate(true);
            int main() {
                if (Read(42) != 11 || Read(true) != 22) return 1;
                if (Choose(1) != 31 || Choose(true) != 37 || Choose<int>(1) != 37) return 2;
                if (Apply(1) != 41 || Apply(1, true) != 43 || Apply<int>(1) != 41 || Apply<int, bool>(1, true) != 43) return 3;
                int value = 1;
                if (Family(value) != 51 || Family(&value) != 53 || Family(true) != 59) return 4;
                if (Family<int>(&value) != 53 || Family<int>(value) != 51) return 5;
                int(int)* integer = Read;
                int(bool)* boolean = &Read;
                if (integer(0) != 11 || boolean(false) != 22 || Invoke(Read) != 11 || Invoke(&Read) != 11) return 6;
                int(int)* ordinary = Choose;
                int(bool)* generic = Choose;
                if (ordinary(0) != 31 || generic(false) != 37) return 7;
                if (WithDefault(10) != 15 || WithDefault(true) != 7) return 8;
                if (Left::Read(0) != 11 || Right::Read(false) != 22 || Left::Family<int>(&value) != 53) return 9;
                if (FoldedInteger != 101 || FoldedBoolean != 103) return 10;
                immutable(char*) name = nameof(Read);
                if (name[0] != 'R' || name[4] != '\0') return 11;
                immutable(char*) templateName = nameof(Identity);
                if (templateName[0] != 'I' || templateName[8] != '\0') return 12;
                return 0;
            }
            """));
        Run(sources, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EachImportedDeclarationUsesItsOwnTypeImportsAndAliases(int optimization)
        => Run([
            new("types.gf", """
                namespace FirstTypes { struct Item { public int Value; public Item(int value) { Value = value; } } }
                namespace SecondTypes { struct Item { public bool Value; public Item(bool value) { Value = value; } } }
                """),
            new("first.gf", """
                using FirstTypes;
                namespace First { int Inspect<T>(Item item, T witness) => item.Value; }
                """),
            new("second.gf", """
                using SecondTypes;
                namespace Second { int Inspect<T>(Item item, T witness) { if (item.Value) return 22; return 0; } }
                """),
            new("caller.gf", """
                using First; using Second;
                namespace Caller {
                    alias Item = bool;
                    public int Test() {
                        FirstTypes::Item first = new(11);
                        SecondTypes::Item second = new(true);
                        if (Inspect(first, 0) != 11 || Inspect(second, false) != 22) return 1;
                        if (Inspect(second, 0) != 22 || Inspect(first, false) != 11) return 2;
                        return 0;
                    }
                }
                int main() => Caller::Test();
                """)
        ], optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ADeclarationInTheNearestLexicalScopeShadowsImportedFamilies(int optimization)
        => Run([new("lexical.gf", """
            using Left; using Right;
            namespace Left { int Read(int value) => 11; }
            namespace Right { int Read<T>(T value) => 22; }
            namespace Local {
                int Read(bool value) => 42;
                public int Test() => Read(true);
            }
            int main() { if (Local::Test() != 42 || Left::Read(0) != 11 || Right::Read(0) != 22) return 1; return 0; }
            """)], optimization);

    [Theory]
    [InlineData("int Pick(int value) => 1;", "int Pick(int value) => 2;", "Pick(0)")]
    [InlineData("int Pick<T>(T value) => MissingLeft();", "int Pick<T>(T value) => MissingRight();", "Pick(0)")]
    [InlineData("int Pick(int value) => 1;", "int Pick(int value) => 2;", "Invoke(Pick)")]
    public void EquallyApplicableImportedDeclarationsRemainAmbiguous(string first, string second, string call)
    {
        string source = "using First; using Second; namespace First { " + first + " } namespace Second { " + second
            + " } int Invoke(int(int)* callback) => callback(0); int main() => " + call + ";";
        if (call == "Invoke(Pick)")
        {
            Compiler.Check(source.Replace(call, "Invoke(First::Pick)", StringComparison.Ordinal));
            Compiler.Check(source.Replace(call, "Invoke(Second::Pick)", StringComparison.Ordinal));
        }
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown function 'Missing", error.Message);
    }

    [Fact]
    public void AnInapplicableLexicalDeclarationDoesNotFallBackToAnImportedOverload()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            using Imported;
            namespace Imported { int Read(int value) => 11; }
            namespace Local { int Read(bool value) => 22; int Test() => Read(1); }
            """));
        Assert.DoesNotContain("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnImportedMethodGroupNeedsATargetBeforeItCanBecomeAFunctionPointer()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            using First; using Second;
            namespace First { int Read(int value) => 11; }
            namespace Second { int Read(bool value) => 22; }
            int main() { var callback = Read; return 0; }
            """));

    private static void Run(IEnumerable<SourceFile> sources, int optimization)
    {
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Imported overload exit {result.ExitCode}: {result.StandardError}");
    }
}
