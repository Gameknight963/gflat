using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class InterfaceRequirementTests
{
    [Theory]
    [InlineData("interface A : B {} interface B { int Read(); }", "must explicitly declare")]
    [InlineData("interface A : B { long Read(); } interface B { int Read(); }", "does not satisfy")]
    [InlineData("interface A : B { int Read(int x); } interface B { int Read(); }", "does not satisfy")]
    [InlineData("interface A : B { int Read(); } interface B { int Read() nothrow; }", "does not satisfy")]
    [InlineData("interface A : B { int Read(); } interface B { immutable int Read(); }", "does not satisfy")]
    [InlineData("interface A : B { int Value { get; } } interface B { ref int Value { get; } }", "required member")]
    [InlineData("interface A : B {} interface B : A {}", "Circular interface requirement")]
    [InlineData("interface A : B, B {} interface B {}", "Duplicate interface requirement")]
    [InlineData("interface A : B {} struct B {}", "must name an interface")]
    [InlineData("interface A : B {} static interface B {}", "static contract")]
    public void InvalidContractsFailAtTheInterfaceEvenWithoutImplementors(string declarations, string diagnostic)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations + " int main() => 0;"));
        Assert.Contains(diagnostic, error.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReorderedDiamondContractsConvertAndDispatchWithoutCopying(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            interface Root { int Read(); }
            interface Left : Root { int LeftValue(); int Read(); }
            interface Right : Root { int Read(); int RightValue(); }
            interface Both : Left, Right { int RightValue(); int Read(); int LeftValue(); }
            struct Reader : Both {
                public int Value;
                public int Read() => Value;
                public int LeftValue() => Value + 1;
                public int RightValue() => Value + 2;
            }
            int ThroughRoot<T : Root>(T value) => value.Read();
            int main() {
                Reader reader = new Reader(); reader.Value = 39;
                Both* both = &reader;
                Left* left = both;
                Right* right = both;
                Root* root = left;
                Root* direct = both;
                Root* other = right;
                if (root.Read() != 39 || direct.Read() != 39 || other.Read() != 39) return 1;
                if (left.LeftValue() != 40 || right.RightValue() != 41) return 2;
                if (ThroughRoot(both) != 39 || ThroughRoot(&reader) != 39) return 3;
                reader.Value = 42;
                return root.Read();
            }
            """, optimizationLevel: optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NullConversionsAndGenericPropertyContractsRetainTheirIdentity(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            namespace Contracts {
                interface Value<T> { T Current { immutable get; } }
                alias IntValue = Value<int>;
                interface Stream<T> : Value<T> { T Current { immutable get; } bool Advance(); }
            }
            struct Reader : Contracts::Stream<int> {
                public int Current { immutable get => 42; }
                public bool Advance() => false;
            }
            int main() {
                Contracts::Stream<int>*? empty = null;
                Contracts::IntValue*? converted = empty;
                if (converted != null) return 1;
                Reader reader = new Reader();
                Contracts::Stream<int>* stream = &reader;
                Contracts::IntValue* value = stream;
                return value.Current;
            }
            """, optimizationLevel: optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassRequirementsRetainVirtualDispatchThroughBasePointers(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            interface Root { int Read(); }
            interface View : Root { int Extra(); int Read(); }
            class Base : View {
                public virtual int Read() => 1;
                public int Extra() => 7;
            }
            class Derived : Base { public override int Read() => 42; }
            int main() {
                Derived instance = new Derived();
                Base* baseView = &instance;
                View* view = baseView;
                Root* root = view;
                if (view.Extra() != 7) return 1;
                return root.Read();
            }
            """, optimizationLevel: optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StaticAndPrimitiveProvidersSatisfyRequiredConstraints(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            static interface RootFactory { int Create(); }
            static interface Factory : RootFactory { int Create(); }
            static class Provider : Factory { public static int Create() => 42; }
            int Make<T : RootFactory>() => T::Create();
            interface Root { immutable int Read(); }
            interface View : Root {
                immutable int Read();
                implementation int { public immutable int Read() => *this; }
            }
            int Read<T : Root>(ref immutable(T) value) => value.Read();
            int main() {
                int value = Make<Provider>();
                View* view = &value;
                readonly(Root)* root = view;
                if (Read(ref value) != 42) return 1;
                return root.Read();
            }
            """, optimizationLevel: optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConvertedInterfacePointersRetainTheConcreteDestructor(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int destroyed = 0;
            interface Root { int Read(); }
            interface View : Root { int Extra(); int Read(); }
            struct Reader : View {
                public int Read() => 42;
                public int Extra() => 1;
                ~Reader() { destroyed++; }
            }
            int main() {
                View* view = new* Reader();
                Root* root = view;
                if (root.Read() != 42) return 1;
                delete root;
                if (destroyed != 1) return 2;
                return 42;
            }
            """, optimizationLevel: optimization);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManagedRequirementsConvertWithoutChangingTheReferencedObject(int optimization)
    {
        string program = """
            alias GC = Collector;
            static class Collector : Standard::IGarbageCollector {
                private static ulong[8] storage;
                public static int Roots;
                public static void* Allocate(nuint bytes, nuint alignment) {
                    if (bytes > (nuint)sizeof(ulong[8]) || alignment > (nuint)alignof(ulong)) throw new Exception("Storage too small");
                    return &storage[0];
                }
                public static void ReferenceAdded(void* slot) nothrow { Roots++; }
                public static void ReferenceRemoved(void* slot) nothrow { Roots--; }
                public static void RegisterDestructor(void* object, void(void*)* destructor) nothrow {}
                public static nuint Collect() => 0;
            }
            interface Root { int Read(); }
            interface View : Root { int Extra(); int Read(); }
            class Reader : View {
                private int value;
                public Reader(int input) { value = input; }
                public int Read() => value;
                public int Extra() => 1;
            }
            int main() {
                {
                View^ view = new^ Reader(42);
                Root^ root = view;
                View^? empty = null;
                Root^? converted = empty;
                if (converted != null) return 1;
                if (root.Read() != 42 || Collector::Roots < 2) return 2;
                }
                if (Collector::Roots != 0) return 3;
                return 42;
            }
            """;
        var result = CompilerTestHelper.Run(program, optimization);
        Assert.Equal(42, result.ExitCode);
    }
}
