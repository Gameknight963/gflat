using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class EnclosingTypeQueryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignaturesFieldsAndStaticMethodsUseTheEnclosingType(int optimization)
        => Run("""
            struct Point
            {
                public int X;
                public [:typeof(*this):]*? Next;
                public static const reflection Type = typeof(*this);
                public static [:typeof(this):] Identity([:typeof(this):] value) => value;
                public [:typeof(*this):] Copy() => *this;
                public immutable bool Equals([:typeof(*this):] other) => X == other.X;
            }
            int main()
            {
                Point first = new();
                first.X = 42;
                Point second = first.Copy();
                if (!first.Equals(second)) return 1;
                if (Point::Identity(&first) != &first) return 2;
                second.Next = &first;
                if (second.Next!.X != 42) return 3;
                static if (Point::Type != reflect(Point)) { wrongOwner(); }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericNestedTypesBindTheirOwnSpecializations(int optimization)
        => Run("""
            namespace Library
            {
                struct Box<T>
                {
                    public T Value;
                    public static const reflection Type = typeof(*this);
                    public [:typeof(*this):] Copy() => *this;
                    public struct Node
                    {
                        public T Value;
                        public [:typeof(*this):]*? Next;
                        public static const reflection Type = typeof(*this);
                        public [:typeof(*this):] Copy() => *this;
                    }
                }
            }
            int main()
            {
                Library::Box<int> box = new();
                box.Value = 7;
                Library::Box<int> copy = box.Copy();
                if (copy.Value != 7) return 1;
                Library::Box<int>::Node first = new();
                first.Value = 42;
                Library::Box<int>::Node second = first.Copy();
                second.Next = &first;
                if (second.Value != 42 || second.Next!.Value != 42) return 2;
                static if (Library::Box<int>::Type != reflect(Library::Box<int>)) { wrongBox(); }
                static if (Library::Box<int>::Node::Type != reflect(Library::Box<int>::Node)) { wrongNode(); }
                Library::Box<long> wide = new();
                wide.Value = 99L;
                Library::Box<long> wideCopy = wide.Copy();
                if (wideCopy.Value != 99L) return 3;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InheritedSignaturesRemainBoundToTheirDeclaringClass(int optimization)
        => Run("""
            class Base
            {
                public int Value;
                public [:typeof(this):] Address() => this;
                public static const reflection Type = typeof(*this);
            }
            class Derived : Base { public int Extra; }
            int main()
            {
                Derived value = new();
                value.Value = 42;
                Base* view = value.Address();
                if (view.Value != 42) return 1;
                static if (typeof(value.Address()) != reflect(Base*)) { wrongSignature(); }
                static if (Base::Type != reflect(Base)) { wrongBase(); }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConstantInstanceQueriesAndBodyQualifiersRetainReceiverTypes(int optimization)
        => Run("""
            struct Point
            {
                public int X;
                public const readonly nuint Size() => typeof(*this).Size;
                public readonly bool ReadonlyView()
                {
                    static if (!typeof(*this).IsReadOnly) { lostReadonly(); }
                    static if (typeof(*this).IsImmutable) { unexpectedImmutable(); }
                    return true;
                }
                public immutable bool ImmutableView()
                {
                    static if (!typeof(*this).IsImmutable) { lostImmutable(); }
                    return true;
                }
            }
            int main()
            {
                const Point value = new();
                const nuint size = value.Size();
                static if (size != 4) { wrongSize(); }
                Point mutable = new();
                if (!mutable.ReadonlyView() || !mutable.ImmutableView()) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InterfaceSelfQueriesReferToTheInterface(int optimization)
        => Run("""
            interface IValue
            {
                [:typeof(this):] Echo([:typeof(this):] other);
            }
            struct Value : IValue
            {
                public IValue* Echo(IValue* other) => other;
            }
            int main()
            {
                Value value = new();
                IValue* view = &value;
                if (view.Echo(view) != view) return 1;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExtensionSignaturesUseTheirTargetTypes(int optimization)
        => Run("""
            extension int
            {
                public readonly [:typeof(*this):] Twice() => *this + *this;
            }
            struct Box<T> { public T Value; }
            extension<T> Box<T>
            {
                public [:typeof(*this):] Copy() => *this;
            }
            int main()
            {
                int value = 21;
                if (value.Twice() != 42) return 1;
                Box<int> box = new();
                box.Value = 42;
                Box<int> copy = box.Copy();
                return copy.Value - 42;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveImplementationSignaturesUseThePrimitive(int optimization)
        => Run("""
            interface IAdd
            {
                readonly int Add(int other);
                implementation int
                {
                    public readonly [:typeof(*this):] Add([:typeof(*this):] other) => *this + other;
                }
            }
            int main()
            {
                int value = 20;
                IAdd* view = &value;
                return view.Add(22) - 42;
            }
            """, optimization);

    [Theory]
    [InlineData("const reflection Type = typeof(this); int main() => 0;")]
    [InlineData("int main() { const reflection Type = typeof(*this); return 0; }")]
    [InlineData("struct Point { public static Point* Get() => this; } int main() => 0;")]
    [InlineData("struct Point { public Point* Pointer = this; } int main() => 0;")]
    public void QueriesRequireAnOwnerAndDoNotExposeRuntimeThis(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Contains("'this'", error.Message);
    }

    [Fact]
    public void ACallerCannotSupplyItsReceiverToANamespaceFunctionSignature()
    {
        const string source = """
            [:typeof(*this):] Make<T>() => new T();
            struct Point { public int Call() { Make<int>(); return 0; } }
            int main() => 0;
            """;
        Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
    }

    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, InternalizeFunctions = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MemberIncludesExampleReusesHeadersForPrivateAndGenericFields(int optimization)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Examples", "member-includes");
        var sources = Directory.GetFiles(root, "*.gf").Order()
            .Select(path => new SourceFile(path, File.ReadAllText(path)));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        var result = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        Assert.Equal("""
            Equal points: true
            Equal after clearing: false
            Cleared pair: true
            Point fields:
              x: int
              y: int
            Pair<int> fields:
              first: int
              second: int

            """.Replace("\r\n", "\n"), result.StandardOutput.Replace("\r\n", "\n"));
    }
}
