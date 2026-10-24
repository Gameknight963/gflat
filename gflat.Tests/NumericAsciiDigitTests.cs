using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class NumericAsciiDigitTests
{
    [Theory]
    [InlineData("float", "1\u0662f", '\u0662')]
    [InlineData("double", "1\u0662d", '\u0662')]
    [InlineData("float", "1.\u0662f", '\u0662')]
    [InlineData("double", "1.\u0662d", '\u0662')]
    [InlineData("double", "1.\u0662", '\u0662')]
    [InlineData("float", "1\uff12f", '\uff12')]
    [InlineData("double", "1\uff12d", '\uff12')]
    [InlineData("float", "1.\uff12f", '\uff12')]
    [InlineData("double", "1.\uff12d", '\uff12')]
    [InlineData("double", "1.\uff12", '\uff12')]
    [InlineData("int", "\u0662", '\u0662')]
    [InlineData("int", "\uff12", '\uff12')]
    public void UnicodeDigitsCannotEnterNumericTokensAndKeepTheirOwnSourceSpan(
        string type,string spelling,char digit)
    {
        string text="int main() {\n    "+type+" value = "+spelling+";\n    return 0;\n}";
        var source=new SourceFile("numeric-unicode.gf",text);
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Emit([source]));
        Assert.Same(source,error.Span.Source);
        Assert.Equal(source.Path,error.FilePath);
        Assert.Equal(source.Span(text.IndexOf(digit),1),error.Span);
        Assert.Contains("Unexpected character",error.Description,StringComparison.Ordinal);
        Assert.Contains(digit.ToString(),error.Description,StringComparison.Ordinal);
    }

    [Fact]
    public void UnicodeDigitsRemainValidInsideStringsAndCommentsWithAsciiIdentifiers()
    {
        // Unicode identifiers are not part of the existing lexer contract.
        // Keep its supported ASCII identifier/digit spelling while protecting
        // Unicode string and comment contents from this numeric-only change.
        const string text="""
            // Unicode decimal digits are ordinary comment text: ٢２.
            int main() {
                readonly(char)* text2 = "٢２";
                int value2 = 2;
                return value2 - 2;
            }
            """;
        var source=new SourceFile("unicode-text.gf",text);
        var tokens=Lexer.Tokenize(source);
        Assert.Contains(tokens,token=>token.Kind==TokenKind.StringLiteral
            && token.Text.Contains('\u0662') && token.Text.Contains('\uff12'));
        Assert.Contains(tokens,token=>token.Kind==TokenKind.Identifier && token.Text=="value2");
        Assert.NotEmpty(Compiler.Emit([source]));
    }
}
