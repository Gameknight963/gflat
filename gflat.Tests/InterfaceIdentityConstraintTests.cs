using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class InterfaceIdentityConstraintTests
{
    [Fact]
    public void RawInterfacePointerSatisfiesItsOwnConstraint()
    {
        Compiler.Check("""
            interface IValue { int Read(); }
            struct Value : IValue { public int Read() => 42; }
            int Read<T : IValue>(T value) => value.Read();
            int main()
            {
                Value value = new();
                IValue* view = &value;
                return Read(view);
            }
            """, options: new() { NoStdLib = true });
    }

    [Fact]
    public void GenericInterfaceSpecializationSatisfiesItsOwnConstraintThroughAnAlias()
    {
        Compiler.Check("""
            interface IValue<T> { T Read(); }
            alias IntValue = IValue<int>;
            struct Value : IntValue { public int Read() => 42; }
            int Read<T : IValue<int>>(T value) => value.Read();
            int main()
            {
                Value value = new();
                IntValue* view = &value;
                return Read(view);
            }
            """, options: new() { NoStdLib = true });
    }

    [Fact]
    public void DifferentGenericInterfaceArgumentsDoNotSatisfyConstraint()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            interface IValue<T> { T Read(); }
            struct Value : IValue<long> { public long Read() => 42; }
            int Read<T : IValue<int>>(T value) => 0;
            int main()
            {
                Value value = new();
                IValue<long>* view = &value;
                return Read(view);
            }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("does not satisfy constraint", error.Message);
    }

    [Theory]
    [InlineData("static interface IFactory { int Create(); }")]
    [InlineData("interface IFactory { static int Create(); }")]
    [InlineData("static interface IFactory {}")]
    public void StaticInterfaceIsNotItsOwnProvider(string contract)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(contract + """

            int Read<T : IFactory>() => 0;
            int main() => Read<IFactory>();
            """, options: new() { NoStdLib = true }));
        Assert.Contains("does not satisfy constraint", error.Message);
    }

    [Fact]
    public void RestrictedAliasStillCannotExposeHiddenInterface()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            interface IVisible { int Read(); }
            interface IHidden { int Secret(); }
            struct Value : IVisible, IHidden
            {
                public int Read() => 42;
                public int Secret() => 7;
            }
            weak constrained alias Selected : IVisible = Value;
            struct Box<T : IHidden> {}
            int main() { Box<Selected> value = new(); return 0; }
            """, options: new() { NoStdLib = true }));
        Assert.Contains("does not satisfy constraint", error.Message);
    }
}
