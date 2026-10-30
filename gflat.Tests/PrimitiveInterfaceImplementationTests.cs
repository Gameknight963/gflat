using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class PrimitiveInterfaceImplementationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstrainedExtensionsRecognizePrimitiveImplementations(int optimization)
    {
        var result = CompilerTestHelper.Run(Contract + """
            interface IEqual<T> {
                immutable bool Equal(T other);
                implementation int : IEqual<int> { public immutable bool Equal(int other) => *this == other; }
            }
            int Read<T : IHash>(ref immutable(T) value) => value.Hash();
            extension<T : IHash> T { public immutable int Read() => (*this).Hash(); }
            struct Box<T> { public T value; public Box(T item) { value=item; } }
            bool Same<T : IEqual<T>>(ref Box<T> box, T other) => box.value.Equal(other);
            extension<T : IEqual<T>> Box<T> { public bool Same(T other) => (*this).value.Equal(other); }
            int main() {
                int value=42;
                Box<int> box=new(value);
                if (!box.Same(42)) return 1;
                return value.Read();
            }
            """, optimizationLevel: optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void PrimitiveExtensionPreviewUsesExactContractsWithoutSpecializing()
    {
        var (ast, checker) = Compiler.Check(Contract + """
            interface IEqual<T> {
                immutable bool Equal(T other);
                implementation int : IEqual<int> { public immutable bool Equal(int other) => *this == other; }
            }
            int Read<T : IHash>(T value) => value.Hash();
            extension<T : IHash> T { public int Read() => (*this).Hash(); }
            bool Same<T : IEqual<T>>(T value, T other) => value.Equal(other);
            extension<T : IEqual<T>> T { public bool Same(T other) => (*this).Equal(other); }
            int main() => 0;
            """, options: new() { NoStdLib=true });
        int members=ast.Members.Count;
        for (int i=0; i<5; i++) {
            Assert.Single(checker.GetVisibleExtensions("", [], new gflat.ast.NamedTypeExpression("int", null, 1), true, false, "Read"));
            Assert.Single(checker.GetVisibleExtensions("", [], new gflat.ast.NamedTypeExpression("int", null, 1), true, false, "Same"));
            Assert.Empty(checker.GetVisibleExtensions("", [], new gflat.ast.NamedTypeExpression("long", null, 1), true, false, "Read"));
            Assert.Empty(checker.GetVisibleExtensions("", [], new gflat.ast.NamedTypeExpression("long", null, 1), true, false, "Same"));
        }
        Assert.Equal(members, ast.Members.Count);
    }

    [Fact]
    public void PrimitiveContractsPreserveMutableAndReadonlyReferenceGetterSelection()
    {
        var result = CompilerTestHelper.Run(Contract + """
            struct Cell<T> {
                private T value;
                public Cell(T item) { value=item; }
                public ref T Item { immutable get => value; }
                public ref T this[int i] { immutable get => value; }
            }
            int main() {
                Cell<int> cell=new(4);
                cell.Item=20;
                cell[0]+=22;
                immutable(Cell<int>)* view=&cell;
                return view.Item+(*view)[0]-42;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(Contract + """
            struct Cell<T> { public T value; public ref T Item { immutable get => value; } }
            int main() { Cell<int> cell=new(); immutable(Cell<int>)* view=&cell; view.Item=5; return 0; }
            """));
    }

    [Fact]
    public void NestedGenericStorageLayoutsRetainTheirDeclarationNamespace()
    {
        var result = CompilerTestHelper.Run(Contract + """
            namespace Library {
                struct Buffer<T> { public T value; public Buffer(T item) { value=item; } }
                struct Owner<T> {
                    public struct Iterator {
                        private Buffer<T> buffer;
                        public Iterator(T item) { buffer=new Buffer<T>(item); }
                        public T Read()=>buffer.value;
                    }
                    public Iterator Create(T item)=>new Iterator(item);
                }
            }
            int main() {
                Library::Owner<int> owner=new();
                Library::Owner<int>::Iterator iterator=owner.Create(42);
                return iterator.Read();
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    private const string Contract = """
        interface IHash {
            immutable int Hash();
            implementation int { public immutable int Hash() => *this; }
        }
        """;

    [Fact]
    public void PrimitivePointerUsesInterfaceTableWithoutBoxing()
    {
        var result = CompilerTestHelper.Run(Contract + "int main() { int n = 42; IHash* p = &n; return p.Hash(); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ConstrainedFunctionCallsThePrimitiveImplementation()
    {
        var result = CompilerTestHelper.Run(Contract + "int Read<T : IHash>(ref immutable(T) n) => n.Hash(); int main() { int n = 42; return Read(ref n); }");
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericInterfaceImplementationNamesAnExactSpecialization()
    {
        var result = CompilerTestHelper.Run("""
            interface IEqual<T> {
                immutable bool Equal(T other);
                implementation int : IEqual<int> { public immutable bool Equal(int other) => *this == other; }
            }
            bool Equal<T : IEqual<T>>(T a, T b) => a.Equal(b);
            int main() { int a = 42; IEqual<int>* p = &a; if (!p.Equal(42)) return 1; if (Equal(42, 42)) return 42; return 0; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void TwoInterfacesCanGiveTheSamePrimitiveDifferentBehavior()
    {
        var result = CompilerTestHelper.Run(Contract + """
            interface IOther {
                immutable int Hash();
                implementation int { public immutable int Hash() => *this + 2; }
            }
            int A<T : IHash>(T n) => n.Hash();
            int B<T : IOther>(T n) => n.Hash();
            int main() { int n = 20; IHash* a = &n; IOther* b = &n; return A(n) + B(n) + a.Hash() - b.Hash() + 2; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("interface I { implementation int {} implementation int {} }", "Duplicate implementation")]
    [InlineData("struct S {} interface I { implementation S {} }", "primitive value type")]
    [InlineData("interface I { implementation void {} }", "primitive value type")]
    [InlineData("interface I { implementation int* {} }", "primitive value type")]
    [InlineData("interface I { int Read(); implementation int {} }", "does not implement")]
    [InlineData("interface I { immutable int Read(); implementation int { public int Read() => 1; } }", "readonly")]
    [InlineData("interface I { int Read(); implementation int { public long Read() => 1; } }", "signature")]
    [InlineData("interface I<T> { implementation int {} }", "closed interface specialization")]
    [InlineData("interface J {} interface I { implementation int : J {} }", "enclosing interface")]
    [InlineData("interface I { implementation int { public int field; } }", "ordinary instance methods")]
    [InlineData("static interface I { implementation int {} }", "instance interface")]
    public void InvalidImplementationsAreRejected(string source, string expected)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(source + " int main() => 0;"));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void ImplementationsDoNotInjectOrdinaryMembers()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(Contract + "int main() { int n = 42; return n.Hash(); }"));
    }

    [Fact]
    public void PrimitiveArithmeticRemainsAvailableWithoutStandardLibrary()
        => Assert.Contains("add i32", Compiler.Emit("int Answer(int n) => n + 1;", options: new() { NoStdLib = true }));

    [Fact]
    public void ConstrainedContainerMethodsAndPointerParametersWork()
    {
        var result = CompilerTestHelper.Run(Contract + """
            struct Holder<T : IHash> {
                public T value;
                public Holder(T input) { value = input; }
                public int Read() => value.Hash();
            }
            int Read<T : IHash>(immutable(T)* value) => value.Hash();
            int main() { Holder<int> h = new(21); int n = 21; return h.Read() + Read(&n); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void PrimitiveConstraintsDoNotLeakIntoOtherGenericDefinitions()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Contract + """
            int Unconstrained<U>(U value) => value.Hash();
            int Outer<T : IHash>(T value) => Unconstrained(value);
            int main() => Outer(42);
            """));
        Assert.Contains("has no applicable", error.Message);
        Assert.Contains("Hash", error.Message);
    }

    [Fact]
    public void AConstrainedCalleeDoesNotExposeItsConstraintsToTheCaller()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(Contract + """
            T Echo<T : IHash>(T n) => n;
            int main() => Echo(42).Hash();
            """));
    }

    [Fact]
    public void GenericIdentityResultsRetainTheCallersOwnContract()
    {
        var result = CompilerTestHelper.Run(Contract + """
            U Echo<U>(U n) => n;
            int Read<T : IHash>(T n) => Echo(n).Hash();
            int main() => Read(42);
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericContainersProjectFieldsAndResultsIntoTheCallersScope()
    {
        var result = CompilerTestHelper.Run(Contract + """
            interface IOther { immutable int Hash(); implementation int { public immutable int Hash() => *this + 1; } }
            struct Box<T : IHash> { public T value; public Box(T n) { value=n; } public T Get() => value; }
            int Read<T : IOther>(Box<T> box) => box.Get().Hash() + box.value.Hash();
            int main() { Box<int> box=new(20); return Read<int>(box); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("box.Get().Hash()")]
    [InlineData("box.value.Hash()")]
    public void AContainerDoesNotExposeItsGenericConstraintsToItsConsumers(string expression)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(Contract + """
            struct Box<T : IHash> { public T value; public Box(T n) { value=n; } public T Get() => value; }
            """ + "int main() { Box<int> box=new(42); return " + expression + "; }"));
    }

    [Fact]
    public void ImplementationMethodsCanCallOtherMethodsOfTheirContract()
    {
        var result = CompilerTestHelper.Run("""
            interface I { immutable int Read(); immutable int Value();
                implementation int { public immutable int Read() => this.Value(); public immutable int Value() => *this; }
            }
            int main() { int n=42; I* p=&n; return p.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryPrimitiveHasNativeLayoutAndUsableInterfaceDispatch(int optimization)
    {
        string[] types = ["byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint", "float", "double", "extralong", "char", "bool"];
        string implementations = string.Join("\n", types.Select(t => "implementation " + t + " { public immutable int Read() { " +
            (t == "bool" ? "if (*this) return 42; return 0;" : "return (int)*this;") + " } }"));
        string checks = string.Join("\n", types.Select((t, i) => t + " n" + i + " = " + (t == "bool" ? "true" : "(" + t + ")42") + "; I* p" + i + " = &n" + i + "; if (p" + i + ".Read() != 42) return 1;"));
        var result = CompilerTestHelper.Run("interface I { immutable int Read(); " + implementations + " } int main() { " + checks + " return 42; }", optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ThrowingPrimitiveMethodsUnwindThroughInterfaceCalls()
    {
        var result = CompilerTestHelper.Run("""
            interface I { int Read(); implementation int { public int Read() { throw new Exception("primitive", *this); } } }
            int main() { int n=42; I* p=&n; try { return p.Read(); } catch (Exception* error) { return error.GetCode(); } }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ReadonlyContractsCannotMutateThePrimitive()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit("interface I { immutable int Read(); implementation int { public immutable int Read() { *this=1; return 42; } } } int main()=>0;"));

    [Fact]
    public void AReadonlyBorrowCannotCallAMutablePrimitiveMethod()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit("interface I { void Update(); implementation int { public void Update() { *this=1; } } } void Read<T:I>(ref immutable(T) n) { n.Update(); } int main() { int n=0; Read(ref n); return 0; }"));

    [Fact]
    public void ContractDefaultsApplyToConstrainedAndInterfaceCalls()
    {
        var result = CompilerTestHelper.Run("""
            interface I { immutable int Read(int extra = 2);
                implementation int { public immutable int Read(int extra = 99) => *this + extra; }
            }
            int Read<T : I>(T value) => value.Read();
            int main() { int value = 19; I* p = &value; return Read(value) + p.Read(); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void MutableImplementationsBorrowTheOriginalValue()
    {
        var result = CompilerTestHelper.Run("""
            interface I { void Add(int n);
                implementation int { public void Add(int n) { *this += n; } }
            }
            void Add<T : I>(ref T value) { value.Add(21); }
            int main() { int n = 0; Add(ref n); I* p = &n; p.Add(21); return n; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void DeclaredAndPrimitiveImplementationsShareTheSameContract()
    {
        var result = CompilerTestHelper.Run(Contract + """
            struct Number : IHash { public immutable int Hash() => 21; }
            int Read<T : IHash>(T value) => value.Hash();
            int main() { Number n = new(); return Read(n) + Read(21); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ImplementationAvailabilityDoesNotDependOnUsingDirectives()
    {
        var result = CompilerTestHelper.Run(new[] {
            new SourceFile("hash.gf", "namespace Hashing { " + Contract + " }"),
            new SourceFile("main.gf", "int Read<T : Hashing::IHash>(T n) => n.Hash(); int main() { int n=21; Hashing::IHash* p=&n; return Read(n)+p.Hash(); }")
        });
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericSpecializationsStayIndependent()
    {
        var result = CompilerTestHelper.Run("""
            interface IEqual<T> { immutable bool Equal(T other);
                implementation int : IEqual<int> { public immutable bool Equal(int other) => *this == other; }
                implementation long : IEqual<long> { public immutable bool Equal(long other) => *this == other; }
            }
            bool Same<T : IEqual<T>>(T a, T b) => a.Equal(b);
            int main() { if (!Same(42,42)) return 1; if (!Same(42l,42l)) return 2;
                int n=42; IEqual<int>* p=&n; if (!p.Equal(42)) return 3; return 42; }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}
