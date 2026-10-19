using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericInterfaceTests
{
    [Fact]
    public void InterfaceSpecializationsRetainDeclarationAttributes()
    {
        var (ast, _) = Compiler.Check("""
            class Marker : Attribute {}
            [Marker] interface IValue<T> { T Read(); }
            struct Value : IValue<int> { public int Read() => 42; }
            int main() => 0;
            """);
        var specialization = ast.Members.OfType<gflat.ast.InterfaceDeclaration>().Single(i => i.Name.StartsWith("IValue$", StringComparison.Ordinal));
        Assert.Single(specialization.Attributes);
    }
    [Fact]
    public void GenericContractsSupportPropertiesConstraintsAndNamespaceAliases()
    {
        var result = CompilerTestHelper.Run("""
            namespace Contracts {
                interface IValue<T> { T Value { readonly get; } }
                alias IntValue = IValue<int>;
                struct Box<T> : IValue<T> {
                    private T value;
                    public Box(T input) { value = input; }
                    public T Value { readonly get => value; }
                }
                T Read<T : IValue<int>>(T value) => value;
            }
            int main() {
                Contracts::Box<int> value = new Contracts::Box<int>(42);
                Contracts::IntValue* pointer = &value;
                if (pointer.Value != 42) return 1;
                return Contracts::Read(value).Value;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void NestedImplementationsKeepTheirSubstitutedBaseTypes()
    {
        var result = CompilerTestHelper.Run("""
            interface IValue<T> { T Read(); }
            struct Container<T> {
                public struct Inner : IValue<T> {
                    private T value;
                    public Inner(T input) { value = input; }
                    public T Read() => value;
                }
            }
            int main() {
                Container<int>::Inner value = new Container<int>::Inner(42);
                IValue<int>* pointer = &value;
                return pointer.Read();
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void InterfaceSpecializationsAreInvariant()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IValue<T> { T Read(); }
            struct Value : IValue<int> { public int Read() => 42; }
            int main() { Value value = new Value(); IValue<long>* pointer = &value; return 0; }
            """));
    }

    [Fact]
    public void WrongGenericArityHasASourceDiagnostic()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("interface IValue<T> { T Read(); } int main() { IValue<int, long>*? value = null; return 0; }"));
        Assert.Contains("expects 1 type arguments, but got 2", error.Message);
    }

    [Fact]
    public void StaticGenericImplementationsCanSatisfyGenericContracts()
    {
        var result = CompilerTestHelper.Run("""
            static interface IFactory<T> { T Identity(T input); }
            static class Factory<T> : IFactory<T> { public static T Identity(T input) => input; }
            int Make<F : IFactory<int>>() => F::Identity(42);
            int main() => Make<Factory<int>>();
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void ConstraintsCanReferToOtherTypeArguments(string kind)
    {
        var result = CompilerTestHelper.Run($$"""
            interface IValue<T> { T Read(); }
            struct Value : IValue<int> { public int Read() => 42; }
            {{kind}} Box<T, V : IValue<T>> {
                public T Read(V value) => value.Read();
            }
            T Read<T, V : IValue<T>>(V value) => value.Read();
            int main() {
                Value value = new Value();
                Box<int, Value> box = new Box<int, Value>();
                if (Read<int, Value>(value) != 42) return 1;
                return box.Read(value);
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericStaticContractsUseOrdinaryProviderConstraints()
    {
        var result = CompilerTestHelper.Run("""
            static interface IFactory<T> { T Create(); }
            static class Factory : IFactory<int> { public static int Create() => 42; }
            int Make<F : IFactory<int>>() => F::Create();
            int main() => Make<Factory>();
            """);
        Assert.Equal(42, result.ExitCode);
    }
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void GenericInterfacesDispatchThroughConcreteImplementations(string kind)
    {
        var result = CompilerTestHelper.Run($$"""
            interface IValue<T> { T Read(); }
            {{kind}} Value<T> : IValue<T> {
                private T value;
                public Value(T input) { value = input; }
                public T Read() => value;
            }
            int Read(IValue<int>* value) => value.Read();
            int main() { Value<int> value = new Value<int>(42); return Read(&value); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericInterfaceMembersAreCheckedAfterSubstitution()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            interface IValue<T> { T Read(); }
            struct Wrong : IValue<int> { public long Read() => 1; }
            int main() => 0;
            """));
        Assert.Contains("return type", error.Message);
        Assert.Contains("IValue<int>", error.Message);
    }
}
