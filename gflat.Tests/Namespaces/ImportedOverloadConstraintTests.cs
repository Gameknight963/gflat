using gflat.CompileExceptions;

namespace gflat.Tests.Namespaces;

public sealed class ImportedOverloadConstraintTests
{
    private const string Contracts = """
        interface IProvider { static int Read(long value); }
        interface IExtra { static int Read(int value); }
        static class Answer : IProvider, IExtra {
            public static int Read(long value) => 42;
            public static int Read(int value) => 7;
        }
        weak alias Selected : IProvider = Answer;
        struct Handler<P> { }
        """;

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void IrrelevantImportedArityKeepsSingletonForwardingAndCallbackViews(bool reverse, int optimization)
    {
        SourceFile[] sources = Ordered(reverse, """
            namespace Useful {
                int Read<P>(P value) => value.Read();
                bool Search<T>(Bag<T> source, immutable(T)* key,
                    bool(immutable(T)*, immutable(T)*)* equal) => equal(key, key);
            }
            """, """
            namespace Noise {
                int Read<P>(P value, int tag) => MissingWrongArityBody();
                bool Search<T>(Bag<T> source, immutable(T)* key,
                    bool(immutable(T)*, immutable(T)*)* equal, int tag) => MissingWrongArityBody();
            }
            """, """
            struct Bag<T> { }
            struct Value { public int Read() => 42; }
            static class Comparer<T> {
                public static bool Same(immutable(T)* left, immutable(T)* right) => left == right;
            }
            """, """
            int Forward<P>(P value) => Read(value);
            bool ForwardSearch<P>(Bag<P> source, immutable(P)* key)
                => Search<P>(move source, key, Comparer<P>::Same);
            int main() {
                Value value = new();
                if (Forward(value) != 42 || Read(value) != 42) return 1;
                Bag<int> source = new(); int key = 42;
                if (!ForwardSearch(source, &key)) return 2;
                return 0;
            }
            """);
        Run(sources, optimization);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ImportedCandidatesRespectDeclaredGenericAndAliasViewsForCallsAndCallbacks(bool reverse, int optimization)
    {
        SourceFile[] sources = Ordered(reverse, """
            namespace Useful {
                int Pick<P : IProvider>(Handler<P> value, long tag) => P::Read(0l);
                int Apply<P : IProvider>(int(long)* callback, long tag) => callback(0l);
            }
            """, """
            namespace Noise {
                int Pick<P : IExtra>(Handler<P> value, int tag) => P::Read(0);
                int Apply<P : IExtra>(int(int)* callback, int tag) => callback(0);
            }
            """, Contracts, """
            int Forward<P : IProvider>(Handler<P> value) => Pick<P>(value, 0);
            int ForwardCallback<P : IProvider>() => Apply<P>(P::Read, 0);
            int main() {
                Handler<Answer> concrete = new(); Handler<Selected> exposed = concrete;
                if (Pick<Answer>(concrete, 0) != 7 || Apply<Answer>(Answer::Read, 0) != 7) return 1;
                if (Pick<Selected>(exposed, 0) != 42 || Apply<Selected>(Selected::Read, 0) != 42) return 2;
                if (Forward<Answer>(concrete) != 42 || Forward<Selected>(exposed) != 42) return 3;
                if (ForwardCallback<Answer>() != 42 || ForwardCallback<Selected>() != 42) return 4;
                // Repeat after concrete and restricted-view specializations have both warmed.
                if (Apply<Answer>(Answer::Read, 0) != 7 || Apply<Selected>(Selected::Read, 0) != 42) return 5;
                return 0;
            }
            """);
        Run(sources, optimization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedCallbackCannotUseAnInterfaceHiddenByItsConstrainedAlias(bool reverse)
    {
        const string useful = """
            namespace Useful {
                int Apply<P : IProvider>(int(long)* callback, bool tag) => callback(0l);
            }
            """;
        const string hidden = """
            namespace Noise {
                int Apply<P : IExtra>(int(int)* callback) => callback(0);
            }
            """;
        // The same candidate and callback are available when the provider's
        // concrete view is used; rejection below is specifically the alias view.
        Compiler.Check(Ordered(reverse, useful, hidden, Contracts,
            "int main() => Apply<Answer>(Answer::Read);"));
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Ordered(reverse,
            useful, hidden, Contracts, "int main() => Apply<Selected>(Selected::Read);")));
        Assert.Contains("Apply", error.Message);
    }

    private static SourceFile[] Ordered(bool reverse, string useful, string noise, string contracts, string client)
    {
        string imports = reverse ? "using Noise; using Useful;" : "using Useful; using Noise;";
        SourceFile first = new("useful.gf", useful), second = new("noise.gf", noise);
        return [new("contracts.gf", contracts), reverse ? second : first, reverse ? first : second,
            new("client.gf", imports + client)];
    }

    private static void Run(IEnumerable<SourceFile> sources, int optimization)
    {
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
