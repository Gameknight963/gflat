using System.Text;
using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class FormattingTests
{
    [Theory]
    [InlineData("int F(int* p){return * p;}", "return *p;")]
    [InlineData("int* F(ref int value){return & value;}", "return &value;")]
    [InlineData("int F(int value){return ++ value;}", "return ++value;")]
    [InlineData("int F(int value){return -- value;}", "return --value;")]
    [InlineData("int F(int value){value ++;return value;}", "value++;")]
    [InlineData("int F(int** p){return * * p;}", "return **p;")]
    [InlineData("int** F(ref int value){return & & value;}", "return & &value;")]
    [InlineData("int* F(int*? p){return p !;}", "return p!;")]
    [InlineData("int F(int*? p){return p ! [0];}", "return p![0];")]
    public void UnaryOperatorsStayAttachedWithoutMergingDistinctTokens(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("format-prefix.gf"), text);
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Contains(expected, actual);
        Assert.Empty(SourceFormatter.Format(new(source.Path, actual), new()));
    }

    [Theory]
    [InlineData("int F(int a,int b){return a*b;}", "a * b")]
    [InlineData("int F(int a,int b){return a&b;}", "a & b")]
    [InlineData("int F(int a,int b){return a^b;}", "a ^ b")]
    [InlineData("int F(int a,int b){return a>>b;}", "a >> b")]
    [InlineData("bool F(int a,int b)=>a<b;", "a < b")]
    [InlineData("bool F(int a,int b)=>a>b;", "a > b")]
    [InlineData("int F(int a,int b){return a* -b;}", "a * -b")]
    [InlineData("int* F(int* p)=>p;", "int* F(int* p)")]
    [InlineData("T Id<T>(T value)=>value; int F()=>Id<int>(1);", "Id<int>(1)")]
    public void BinaryOperatorSpacingPreservesPointerAndGenericSyntax(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("format-binary.gf"), text);
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Contains(expected, actual);
        Assert.Empty(SourceFormatter.Format(new(source.Path, actual), new()));
    }

    [Theory]
    [InlineData("int F(int value){return -value;}", "return -value;")]
    [InlineData("int F(int value){return -  value;}", "return -value;")]
    [InlineData("int F(int value){return - -value;}", "return - -value;")]
    [InlineData("int F(int value){return value-1;}", "return value - 1;")]
    [InlineData("int F(int value){return - (value+1);}", "return -(value + 1);")]
    [InlineData("int F(int value){return (- value);}", "return (-value);")]
    public void UnaryMinusStaysAttachedToItsOperand(string text, string expected)
    {
        var source = new SourceFile(Path.GetFullPath("format-unary.gf"), text);
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Contains(expected, actual);
        Assert.Empty(SourceFormatter.Format(new(source.Path, actual), new()));
    }

    [Fact]
    public void FormatsWhitespaceBeforeEscapedNamesWithoutRemovingTheirEscape()
    {
        var source = new SourceFile(Path.GetFullPath("format-escaped.gf"), "int F(){int    @class=1;@class=2;return @class;}");
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Equal("int F()\n{\n    int @class = 1;\n    @class = 2;\n    return @class;\n}\n", actual);
        Assert.Empty(SourceFormatter.Format(new(source.Path, actual), new()));
    }

    [Fact]
    public void SyntaxOnlySnapshotsRetainFormattingNodesWithoutRunningTheTypeChecker()
    {
        var source = new SourceFile(Path.GetFullPath("syntax-format.gf"), "Missing<int>* Read(){return absent;}");
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true }, checkTypes: false);
        Assert.Null(snapshot.Checker);
        Assert.Contains(snapshot.Nodes, node => node.Node is ast.PointerTypeExpression);
        Assert.Empty(snapshot.Diagnostics);
    }

    private static string Apply(SourceFile source, SourceEdit[] edits)
    {
        string text = source.Text;
        foreach (var edit in edits.OrderByDescending(e => Workspace.Offset(source.Text, JsonSerializer.SerializeToElement(e.Range.Start, Protocol.JsonOptions))))
        {
            int start = Workspace.Offset(source.Text, JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
            int end = Workspace.Offset(source.Text, JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
            text = text.Remove(start, end - start).Insert(start, edit.NewText);
        }
        return text;
    }

    [Fact]
    public void IndexersStayAttachedWhenFormattingStatements()
    {
        var source = new SourceFile(Path.GetFullPath("format-indexer.gf"), "int F(){return map [&s\"One\"];} ");
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Contains("map[&s\"One\"]", actual);
        Assert.Empty(SourceFormatter.Format(new(source.Path, actual), new()));
    }

    [Fact]
    public void ArrayLiteralsKeepTheirLeadingSpaceAndFollowingIndexersStayAttached()
    {
        var source = new SourceFile(Path.GetFullPath("format-array-literal.gf"), "int F(){int[2] values=[3,4];return [1,2] [0];}");
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Contains("values = [3, 4];", actual);
        Assert.Contains("return [1, 2][0];", actual);
    }

    [Fact]
    public void FormatsImportsAndBlocksAndIsIdempotent()
    {
        var source = new SourceFile(Path.GetFullPath("format.gf"), "using A; using B;\nint F(){int a=1;return a+2;}");
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Equal("using A;\nusing B;\n\nint F()\n{\n    int a = 1;\n    return a + 2;\n}\n", actual);
        Assert.Empty(SourceFormatter.Format(new(source.Path, actual), new()));
    }

    [Fact]
    public void KeepsDirectiveBodiesCommentsStringsAndGenericSyntax()
    {
        const string code = "#define CALL(x) ((x)+1)\n// keep  spacing\nstruct Box<T>{T* data;}\nint F(){readonly(char)* text=\"a  b\";return CALL(4);}";
        var source = new SourceFile(Path.GetFullPath("format-preserve.gf"), code);
        string actual = Apply(source, SourceFormatter.Format(source, new()));
        Assert.Contains("#define CALL(x) ((x)+1)\n// keep  spacing", actual);
        Assert.Contains("\"a  b\"", actual);
        var before = Lexer.Tokenize(source).Select(t => (t.Kind, t.Text));
        var after = Lexer.Tokenize(new SourceFile(source.Path, actual)).Select(t => (t.Kind, t.Text));
        Assert.Equal(before, after);
    }

    [Fact]
    public void SupportsTabsAndSameLineBraces()
    {
        var source = new SourceFile(Path.GetFullPath("format-options.gf"), "int F(){return 1;}");
        Assert.Equal("int F() {\n\treturn 1;\n}\n", Apply(source, SourceFormatter.Format(source, new(2, false, false))));
    }

    [Fact]
    public void SelectionFormattingKeepsTextOutsideTheRange()
    {
        const string code = "int F()\n{\n int a=1;\n return a;\n}\n";
        var source = new SourceFile(Path.GetFullPath("format-range.gf"), code);
        var range = new TextRange(new(2, 0), new(2, 9));
        string actual = Apply(source, SourceFormatter.Format(source, new(), range));
        Assert.Equal("int F()\n{\n    int a = 1;\n return a;\n}\n", actual);
    }

    [Fact]
    public void PreservesInterpolatedStrings()
    {
        const string literal = "$\"a  { 1 + 2 } b\"";
        var source = new SourceFile(Path.GetFullPath("format-interpolation.gf"), "int F(){" + literal + ";return 1;}");
        Assert.Contains(literal, Apply(source, SourceFormatter.Format(source, new())));
    }
}
