using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantAggregateCopyTests
{
    private const string Types = """
        struct Pair {
            public int value;
            public const Pair(int input) { value = input; }
            public const int Set(int input) { value = input; return 0; }
        }
        struct Outer {
            public Pair pair;
            public const Outer(Pair input) { pair = input; }
            public const Pair Read() => pair;
        }
        struct Link {
            public Pair* target;
            public const Link(Pair* input) { target = input; }
        }
        class ValueClass { public int value; public const ValueClass(int input) { value = input; } }
        const int Mutate(Pair pair) { pair.value = 99; return 0; }
        const int First(Pair first, int ignored) => first.value;
        const int MutateClass(ValueClass value) { value.value = 99; return 0; }
        """;

    [Theory]
    [InlineData("Pair a = new Pair(seed); Pair b = a; b.value = 99; return a.value;")]
    [InlineData("Pair a = new Pair(seed); Pair b = new Pair(0); b = a; b.value = 99; return a.value;")]
    [InlineData("Pair a = new Pair(seed); Mutate(a); return a.value;")]
    [InlineData("Outer a = new Outer(new Pair(seed)); Outer b = a; b.pair.value = 99; return a.pair.value;")]
    [InlineData("Pair a = new Pair(seed); Pair[1] values = [a]; values[0].value = 99; return a.value;")]
    [InlineData("Pair[1] a = [new Pair(seed)]; Pair[1] b = a; b[0].value = 99; return a[0].value;")]
    [InlineData("Pair[1] values = [new Pair(seed)]; foreach (Pair item in values) { item.value = 99; } return values[0].value;")]
    [InlineData("Outer a = new Outer(new Pair(seed)); Pair b = a.Read(); b.value = 99; return a.pair.value;")]
    [InlineData("Pair a = new Pair(seed); Outer b = new Outer(a); b.pair.value = 99; return a.value;")]
    [InlineData("Pair a = new Pair(seed); return First(a, a.Set(99));")]
    [InlineData("ValueClass a = new ValueClass(seed); MutateClass(a); return a.value;")]
    public void ValueTransfersCopyInlineAggregates(string body)
        => AssertParity(body, 7);

    [Theory]
    [InlineData("Pair a = new Pair(seed); Link first = new Link(&a); Link second = first; second.target.value = 99; return a.value;")]
    [InlineData("Pair a = new Pair(seed); Pair* address = &a; Pair b = new Pair(99); a = b; return address.value;")]
    [InlineData("Outer a = new Outer(new Pair(seed)); Pair* address = &a.pair; Outer b = new Outer(new Pair(99)); a = b; return address.value;")]
    [InlineData("Pair[1] a = [new Pair(seed)]; Pair* address = &a[0]; Pair[1] b = [new Pair(99)]; a = b; return address.value;")]
    [InlineData("Pair a = new Pair(seed); a.Set(99); return a.value;")]
    public void PointerAliasesAndMutatingReceiversRetainTheirStorage(string body)
        => AssertParity(body, 99);

    private static void AssertParity(string body, int expected)
    {
        Assert.Equal(0, CompilerTestHelper.Run(Types + "\nconst int Read(int seed) { " + body + " }\n" + $$"""
            int Input() => 7;
            int main() {
                const int folded = Read(7);
                int runtime = Read(Input());
                if (folded != {{expected}} || runtime != {{expected}}) return 1;
                return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void CopyingAConstantAggregateDoesNotMutateItsStoredValue()
        => Assert.Equal(0, CompilerTestHelper.Run(Types + """
            const Pair original = new Pair(7);
            const int Fold() { Pair copy = original; copy.value = 99; return original.value; }
            int main() { const int folded = Fold(); if (folded != 7 || original.value != 7) return 1; return 0; }
            """).ExitCode);

    [Theory]
    [InlineData("const Number copied = original;")]
    [InlineData("const Number[1] copied = [original];")]
    public void ExistingValuesCannotSilentlySkipUserCopyConstructors(string copy)
    {
        const string declarations = """
            struct Number {
                public int value;
                public const Number(int input) { value = input; }
                public copy Number(immutable(Number)* source) { value = source.value + 1; }
            }
            Number original = new Number(7);
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations + copy + " int main()=>0;"));
        Assert.Contains("Constants cannot contain values with nontrivial copying or destruction", error.Description);
    }

    [Fact]
    public void FreshConstructionDoesNotInvokeACopyConstructorDuringEvaluation()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Number {
                public int value;
                public const Number(int input) { value = input; }
                public copy Number(immutable(Number)* source) { value = source.value + 1; }
            }
            const int Fold() { Number value = new Number(7); return value.value; }
            int main() {
                const int folded = Fold();
                Number original = new Number(7); Number runtime = original;
                if (folded != 7 || original.value != 7 || runtime.value != 8) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ConstantForeachCannotSkipElementCopyConstructors()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Number {
                public int value;
                public const Number(int input) { value = input; }
                public copy Number(immutable(Number)* source) { value = source.value + 1; }
            }
            const int Fold() {
                Number[1] values = [new Number(7)];
                foreach (Number value in values) { return value.value; }
                return 0;
            }
            int main() { const int folded = Fold(); return folded; }
            """));
        Assert.Contains("Nontrivial copying is not supported during constant evaluation", error.Description);
    }

    [Theory]
    [InlineData("Model::Container a = new Model::Container(new Pair(seed)); Model::Container b = a; b.items[0].value = 99; return a.items[0].value;")]
    [InlineData("Child a = new Child(new Pair(seed)); Child b = a; b.pair.value = 99; return a.pair.value;")]
    public void AliasedAndInheritedFieldsCopyInTheirDeclaredRepresentation(string body)
    {
        const string declarations = """
            namespace Model {
                alias Values = Pair[1];
                struct Container {
                    public Values items;
                    public const Container(Pair input) { items = [input]; }
                }
            }
            class Parent {
                public Pair pair;
                public const Parent(Pair input) { pair = input; }
            }
            class Child : Parent { public const Child(Pair input) : base(input) {} }
            """;
        Assert.Equal(0, CompilerTestHelper.Run(Types + declarations + "const int Read(int seed) { " + body + " }" + """
            int Input() => 7;
            int main() { const int folded = Read(7); int runtime = Read(Input()); if (folded != 7 || runtime != 7) return 1; return 0; }
            """).ExitCode);
    }

    [Fact]
    public void RefArgumentsHaveAnExplicitConstantEvaluationDiagnostic()
    {
        const string source = """
            const int Change(ref int value) { value = 99; return value; }
            const int Fold() { int value = 7; Change(ref value); return value; }
            int main() { const int result = Fold(); return result; }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("ref parameters are not supported", error.Description);
    }

    [Fact]
    public void RefArgumentsStillMutateAtRuntime()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            const int Change(ref int value) { value = 99; return value; }
            int main() { int value = 7; Change(ref value); if (value != 99) return 1; return 0; }
            """).ExitCode);
}
