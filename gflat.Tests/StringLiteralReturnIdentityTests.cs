using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class StringLiteralReturnIdentityTests
{
    [Theory]
    [InlineData("struct", "short")]
    [InlineData("class", "short")]
    [InlineData("struct", "qualified")]
    [InlineData("class", "qualified")]
    [InlineData("struct", "alias")]
    [InlineData("class", "alias")]
    public void NestedLiteralFactoriesResolveTheirDeclaredOwner(string kind, string spelling)
        => Compiler.Check(Factory("Library", kind, ReturnType("Library", spelling)) + "int main()=>Library::Run();",
            options: new() { NoStdLib = true });

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void SameLeafAliasToAnUnrelatedTypeIsRejected(string kind)
    {
        string source = "struct Item{} alias RootItem=Item;" + Factory("Library", kind, "RootItem") + "int main()=>Library::Run();";
        var diagnostics = Compiler.Analyze([new SourceFile("literal-return.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("A string literal operator must return its containing class or struct", error.Message);
        Assert.Equal("literal-return.gf", error.FilePath);
    }

    [Theory]
    [InlineData("struct", "readonly")]
    [InlineData("class", "readonly")]
    [InlineData("struct", "immutable")]
    [InlineData("class", "immutable")]
    public void ResolvedAliasReturnQualifiersSurviveOnTheLiteral(string kind, string qualifier)
    {
        string resultType = qualifier + "(Self)";
        string source = Factory("Library", kind, resultType) + "int main()=>Library::Run();";
        var snapshot = new AnalysisSnapshot([new SourceFile("qualified-literal.gf", source)], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var literal = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<PrefixedStringLiteralExpression>());
        var type = Assert.IsType<NamedTypeExpression>(snapshot.Checker!.GetType(literal));
        Assert.True(type.IsReadOnlyValue);
        Assert.Equal(qualifier == "immutable", type.IsImmutableValue);
        Assert.Equal("Library::Outer::Item", snapshot.Checker.DisplayName(type.Name));

        string mutation = Factory("Library", kind, resultType, mutate: true) + "int main()=>Library::Run();";
        var errors = Compiler.Analyze([new SourceFile("literal-mutation.gf", mutation)], new() { NoStdLib = true });
        Assert.Contains(errors, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.Message == "Cannot assign to field 'Value' on readonly instance");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeclaredOwnerAndQualifierFactoriesRun(int optimization)
    {
        var declarations = new List<string>();
        var calls = new List<string>();
        void Add(string kind, string spelling)
        {
            string ns = "Case" + declarations.Count;
            declarations.Add(Factory(ns, kind, ReturnType(ns, spelling)));
            calls.Add($"if({ns}::Run()!=2)return {calls.Count + 1};");
        }
        foreach (string kind in new[] { "struct", "class" })
        {
            foreach (string spelling in new[] { "short", "qualified", "alias", "readonly", "immutable" })
                Add(kind, spelling);
        }
        string source = string.Join("\n", declarations) + "int main(){" + string.Join("", calls) + "return 0;}";
        var result = CompilerTestHelper.Run(source, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        Assert.Empty(result.StandardOutput);
    }

    private static string ReturnType(string ns, string spelling) => spelling switch {
        "short" => "Item",
        "qualified" => ns + "::Outer::Item",
        "alias" => "Self",
        _ => spelling + "(Self)"
    };

    private static string Factory(string ns, string kind, string returnType, bool mutate = false)
    {
        string mutation = mutate ? "value.Value=9;" : "";
        return $$"""
            namespace {{ns}}{
                alias Self=Outer::Item;
                class Outer{
                    public {{kind}} Item{
                        public int Value;
                        public Item(int n){Value=n;}
                        public static {{returnType}} operator lit""(readonly(char)* data,nuint length){
                            return new Outer::Item((int)length);
                        }
                    }
                }
                public int Run(){var value=lit"ab";{{mutation}}return value.Value;}
            }
            """;
    }
}
