namespace gflat.Tests.Namespaces;

public sealed class DestructorFieldContextTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void GenericReturnsInspectUnboundFieldsInTheirDeclarationContext(bool shadowSpan, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(shadowSpan, false), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedFieldsRetainTheirPerFileAliasAndImportContext(bool shadowSpan)
        => Compiler.Check(Sources(shadowSpan, true));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ClassAndInheritedFieldsUseTheirDeclaringOwnersContext(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(false, false, asClass: true), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private static IEnumerable<SourceFile> Sources(bool shadowSpan, bool alias, bool asClass = false)
    {
        // Resolving TextLines.AsEnumerable's signature checks Enumerable's body
        // before TextLines/Enumerator fields receive their ordinary body pass.
        // The generic helper deliberately cannot see this file's memory import.
        string root = Path.Combine(AppContext.BaseDirectory, "Standard/core");
        foreach (string relative in new[] { "Collections/IEnumerable.gf", "Collections/IEnumerator.gf",
            "Queries/Enumerable.gf", "Memory/Span.gf", "Text/TextLines.gf" })
        {
            string path = Path.Combine(root, relative);
            string text = File.ReadAllText(path);
            if (relative == "Memory/Span.gf")
            {
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(text,
                    "(?m)^namespace Standard(?:::Memory)?;\\r?$"));
                text = text.Replace("namespace Standard;", "namespace Standard::FieldMemory;", StringComparison.Ordinal)
                    .Replace("namespace Standard::Memory;", "namespace Standard::FieldMemory;", StringComparison.Ordinal);
            }
            if (relative == "Text/TextLines.gf")
            {
                text = "using Standard::FieldMemory;\n" + text
                    .Replace("using Standard::Memory;", "", StringComparison.Ordinal)
                    .Replace("using Standard::Queries;", "", StringComparison.Ordinal);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\bEnumerable<", "Standard::Queries::Enumerable<");
                if (alias)
                {
                    text = text.Replace("Span<readonly(char)>", "View", StringComparison.Ordinal)
                        .Replace("namespace Standard::Text;", "namespace Standard::Text;\nalias View = Span<readonly(char)>;", StringComparison.Ordinal);
                }
                if (asClass)
                {
                    text = text.Replace("public struct TextLines :", "public class TextLines :", StringComparison.Ordinal);
                    text += """

                        public class DerivedTextLines : TextLines {
                            public DerivedTextLines(Span<readonly(char)> text) : base(text) {}
                        }
                        """;
                }
            }
            if (relative == "Queries/Enumerable.gf" && shadowSpan)
                text += "\nstruct Span<T> { ~Span() {} }\n";
            yield return new SourceFile(path, text);
        }
        string program = """
            using Standard;
            using Standard::Text;
            using Standard::FieldMemory;
            int main() {
                char[3] data = ['a', '\n', 'b'];
                Span<readonly(char)> view = new(data, 3);
                TextLines lines = new(view);
                var query = lines.AsEnumerable();
                var source = query.SourceView();
                var cursor = source.GetEnumerator();
                if (!cursor.MoveNext() || cursor.Current[0] != 'a') return 1;
                if (!cursor.MoveNext() || cursor.Current[0] != 'b') return 2;
                if (cursor.MoveNext()) return 3;
                return 0;
            }
            """;
        if (asClass)
            program = program.Replace("var query = lines.AsEnumerable();", "DerivedTextLines derived = new(view); var query = derived.AsEnumerable();", StringComparison.Ordinal);
        yield return new SourceFile("destructor-field-context-main.gf", program);
    }
}
