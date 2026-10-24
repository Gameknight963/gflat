using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AliasSpecializationProofTests
{
    private const string OverloadedProvider = """
        interface IProvider { static int Read(long value); }
        static class Answer : IProvider {
            public static int Read(long value) => 42;
            public static int Read(int value) => 7;
        }
        weak alias Selected : IProvider = Answer;
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedDuckTemplateCallsMustExposeTheSelectedOverload(bool warmConcrete)
    {
        string source = OverloadedProvider + $$"""
            int Forward<P>() => P::Read(0);
            int main() {
                {{(warmConcrete ? "Forward<Answer>();" : "")}}
                return Forward<Selected>();
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Read", error.Message);
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedGenericDependenciesBelongToTheirNominalEmittedBody(bool safeBodyLast)
    {
        const string unsafeCall = "Forward<A,Answer>(a,concrete);";
        const string safeCall = "Forward<B,Answer>(b,concrete);";
        string source = $$"""
            interface IProvider { static int Read(); }
            interface IExtra { static int Extra(); }
            static class Answer : IProvider, IExtra {
                public static int Read() => 42;
                public static int Extra() => 7;
            }
            weak alias Selected : IProvider = Answer;
            struct Handler<P> { }
            struct A { public int Convert<P>(Handler<P> value) => P::Extra(); }
            struct B { public int Convert<P>(Handler<P> value) => 0; }
            int Forward<C,P>(C receiver, Handler<P> value) => receiver.Convert(value);
            int main() {
                A a = new(); B b = new(); Handler<Answer> concrete = new();
                {{(safeBodyLast ? unsafeCall + safeCall : safeCall + unsafeCall)}}
                Handler<Selected> contract = concrete;
                return Forward<A,Selected>(a,contract);
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Extra", error.Message);
        Assert.Contains("not exposed", error.Message);
    }

    [Fact]
    public void AliasValidationDuringRecursiveCheckingWaitsForTheCompletedProof()
    {
        string source = OverloadedProvider + """
            int Forward<P>(bool recurse) {
                if (recurse) return Forward<Selected>(false);
                return P::Read(0);
            }
            int main() => Forward<Answer>(true);
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Read", error.Message);
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitLocalAliasesRetainResolvedDependencyProvenance(bool warmConcrete)
    {
        string source = $$"""
            interface IProvider { static int Read(); }
            interface IExtra { static int Extra(); }
            static class Answer : IProvider, IExtra {
                public static int Read() => 42;
                public static int Extra() => 7;
            }
            weak alias Selected : IProvider = Answer;
            int Hidden<P>() => P::Extra();
            int Forward<P>() {
                alias Local = P;
                return Hidden<Local>();
            }
            int main() {
                {{(warmConcrete ? "Forward<Answer>();" : "")}}
                return Forward<Selected>();
            }
            """;
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("Extra", error.Message);
        Assert.Contains("not exposed", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassAliasContractsExcludePrivateOverloadsBeforeRanking(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            class Contract {
                public static int Read(long value) => 42;
                private static int Read(int value) => 7;
            }
            weak alias Selected : Contract = Contract;
            int main() => Selected::Read(0);
            """, optimization);
        Assert.Equal(42, result.ExitCode);
    }
}
