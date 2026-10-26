namespace gflat.Tests.Namespaces;

public sealed class DeclaredMemberSignatureContextTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 2)]
    public void EarlySpecializationUsesImplementationDeclarationTypes(bool reverseSources, bool useAlias, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(reverseSources, useAlias, early: true), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryBodySpecializationIsTheSameDeclaredCall(bool useAlias)
        => Compiler.Check(Sources(reverseSources: false, useAlias, early: false));

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void NonGenericContractParameterKeepsItsOwnPerFileAlias(bool reverseSources, int optimization)
    {
        var sources = new[] {
            new SourceFile("nongeneric-contract.gf", """
                namespace Contracts;
                alias Item = Foreign::Key;
                interface IEqual {
                    immutable bool Equals(immutable(Item)* other);
                }
                """),
            new SourceFile("nongeneric-key.gf", """
                namespace Foreign;
                struct Key : Contracts::IEqual {
                    public int value;
                    public immutable bool Equals(immutable(Key)* other) => value == other.value;
                }
                """),
            new SourceFile("nongeneric-helper.gf", """
                namespace Helpers;
                struct Item { public int unrelated; }
                struct Box<T : Contracts::IEqual> {
                    public bool Same(immutable(T)* left, immutable(Foreign::Key)* right) => left.Equals(right);
                }
                """),
            new SourceFile("nongeneric-main.gf", """
                int Trigger(Helpers::Box<Foreign::Key>* box) => 0;
                int main() {
                    Foreign::Key left = new(); left.value = 42;
                    Foreign::Key same = new(); same.value = 42;
                    Foreign::Key different = new(); different.value = 7;
                    Helpers::Box<Foreign::Key> box = new();
                    if (!box.Same(&left, &same)) return 1;
                    if (box.Same(&left, &different)) return 2;
                    return 0;
                }
                """)
        };
        var result = CompilerTestHelper.Run(reverseSources ? sources.Reverse() : sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
    private static IEnumerable<SourceFile> Sources(bool reverseSources, bool useAlias, bool early)
    {
        var contract = new SourceFile("contracts.gf", """
            namespace Contracts;
            interface IEqual<T> {
                immutable bool Equals(immutable(T)* other);
            }
            """);
        string keyText = """
            using Contracts;
            namespace Foreign;
            alias Self = Key;
            struct Key : IEqual<Key> {
                public int value;
                public immutable bool Equals(immutable(Key)* other) => value == other.value;
                public immutable bool Equals(int ignored) => false;
            }
            """;
        if (useAlias)
            keyText = keyText.Replace("immutable(Key)* other", "immutable(Self)* other", StringComparison.Ordinal);
        var key = new SourceFile("foreign-key.gf", keyText);
        var helper = new SourceFile("helper.gf", """
            namespace Helpers;
            // Both names deliberately disagree with the implementation file.
            struct Key { public int unrelated; }
            alias Self = Key;
            struct Box<T : Contracts::IEqual<T>> {
                public bool Same(immutable(T)* left, immutable(T)* right) => left.Equals(right);
            }
            """);
        string main = """
            using Helpers;
            int main() {
                Foreign::Key left = new(); left.value = 42;
                Foreign::Key same = new(); same.value = 42;
                Foreign::Key different = new(); different.value = 7;
                Box<Foreign::Key> box = new();
                if (!box.Same(&left, &same)) return 1;
                if (box.Same(&left, &different)) return 2;
                return 0;
            }
            """;
        // Global parameter signatures are visited before namespace method
        // signatures. This forces Box's declared dispatch to inspect Key's raw
        // signature before its ordinary declaration pass has canonicalized it.
        if (early)
            main += "\nint Trigger(Helpers::Box<Foreign::Key>* box) => 0;\n";
        var sources = new[] { contract, key, helper, new SourceFile("main.gf", main) };
        return reverseSources ? sources.Reverse() : sources;
    }
}
