using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class EnumMemberSpanTests
{
    [Theory]
    [InlineData("enum Status { First, Second }", "First", "Second")]
    [InlineData("enum Status { First = 1 + 2, Second = First << 1 }", "First = 1 + 2", "Second = First << 1")]
    [InlineData("enum Status {\n First,\n Second\n}", "First", "Second")]
    [InlineData("enum Status {\n First = 1 +\n 2,\n Second = (First << 1)\n}", "First = 1 +\n 2", "Second = (First << 1)")]
    public void EachMemberOwnsItsNameThroughInitializerWithoutTheComma(string text,string first,string second)
    {
        var source=new SourceFile("enum-member-spans.gf",text);
        var diagnostics=new DiagnosticBag();
        var unit=Parser.Parse(Lexer.Tokenize(source,diagnostics),diagnostics);
        Assert.False(diagnostics.HasErrors);
        var declaration=Assert.Single(unit.Members.OfType<EnumDeclaration>());
        Assert.Equal(2,declaration.Members.Count);
        foreach(var (member,expected) in declaration.Members.Zip(new[]{first,second})) {
            int start=text.IndexOf(expected,StringComparison.Ordinal);
            Assert.True(start>=0);
            Assert.Same(source,member.Span.Source);
            Assert.Equal(source.Span(start,expected.Length),member.Span);
            Assert.Equal(expected,text.Substring(member.Span.Start,member.Span.Length));
        }
    }
}
