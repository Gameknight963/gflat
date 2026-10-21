using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class LexerSourceContextTests
{
    [Theory]
    [InlineData("int good;\n   @", "@", "Unexpected character")]
    [InlineData("int good;\n   0x;", "0x", "hexadecimal digits")]
    [InlineData("int good;\n   1__2;", "1__2", "Numeric separators")]
    [InlineData("\r\n  /* unfinished\ncomment", "/*", "Unterminated block comment")]
    [InlineData("int good;\n   \"unterminated", "\"", "Unterminated string")]
    [InlineData("int good;\n   'ab'", "'", "Invalid char literal")]
    [InlineData("int good;\n   $\"x }\"", "$", "Unmatched '}'")]
    [InlineData("  int x = $\"{0x}\";", "$", "hexadecimal digits")]
    public void DirectAndHelperErrorsKeepBoundSourceTokenStart(string text,string marker,string message)
    {
        var source=new SourceFile("bound.gf",text);
        var error=Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize(source));
        Assert.Same(source,error.Span.Source);
        Assert.Equal(source.Span(text.IndexOf(marker,StringComparison.Ordinal),1),error.Span);
        Assert.Contains(message,error.Description,StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryRetainsIndependentErrorSpansAndFollowingValidTokens()
    {
        var source=new SourceFile("recovery.gf","int before;\n  @\n int middle = 0x;\n int after;");
        var bag=new DiagnosticBag();var tokens=Lexer.Tokenize(source,bag);
        Assert.Equal(2,bag.Items.Count);
        Assert.Equal(source.Span(source.Text.IndexOf('@'),1),bag.Items[0].Span);
        Assert.Equal(source.Span(source.Text.IndexOf("0x",StringComparison.Ordinal),1),bag.Items[1].Span);
        Assert.Equal(new[]{"before","middle","after"},tokens.Where(t=>t.Kind==TokenKind.Identifier).Select(t=>t.Text));
        Assert.All(tokens,t=>Assert.Same(source,t.Source));
        var after=tokens.Single(t=>t.Text=="after");Assert.Equal(4,after.Line);Assert.Equal(6,after.Column);
    }

    [Fact]
    public void PlainStringLexingDoesNotKeepPreviousBoundSource()
    {
        Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize(new SourceFile("previous.gf","   @")));
        var error=Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize("\n @"));
        Assert.Null(error.Span.Source);Assert.Equal(2,error.Line);
    }

    [Fact]
    public void NestedInterpolationTokensKeepTheOriginalFileAndOffsets()
    {
        const string text="\n  $\"outer { $\"inner {42}\" }\"";
        var source=new SourceFile("nested.gf",text);var tokens=Lexer.Tokenize(source);
        var integer=Assert.Single(tokens,t=>t.Kind==TokenKind.IntLiteral);
        Assert.Equal("42",integer.Text);Assert.Same(source,integer.Source);
        Assert.Equal(source.Span(text.IndexOf("42",StringComparison.Ordinal),2),integer.Span);
        Assert.Equal(2,tokens.Count(t=>t.Kind==TokenKind.InterpolatedStringStart));
    }

    [Fact]
    public void NestedHelperRequestedLineKeepsExistingDiagnosticMapping()
    {
        // Nested expression lexing reports a relative line. Preserve the
        // existing requested-line mapping while changing the context fast path.
        var source=new SourceFile("nested-error.gf","  int before;\n   $\"{0x}\";");
        var error=Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize(source));
        Assert.Equal(source.LineSpan(1),error.Span);
        Assert.Contains("hexadecimal digits",error.Description,StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentLexingKeepsErrorsAndRecoveryInTheirOwnSource()
    {
        await Task.WhenAll(Enumerable.Range(0,16).Select(async index=>{
            await Task.Yield();
            string prefix=new string(' ',index+1);
            var source=new SourceFile($"concurrent-{index}.gf","int before;\n"+prefix+"@\nint after;");
            var error=Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize(source));
            Assert.Same(source,error.Span.Source);Assert.Equal(index+2,error.Span.Column);
            var bag=new DiagnosticBag();var tokens=Lexer.Tokenize(source,bag);
            Assert.Same(source,Assert.Single(bag.Items).Span.Source);
            Assert.All(tokens,t=>Assert.Same(source,t.Source));
            await Task.Yield();
            Assert.Null(Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize("@")).Span.Source);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulLexingAllocationsDoNotScaleWithDiagnosticContextWrites(bool dense)
    {
        var source=new SourceFile("allocation.gf",dense?string.Concat(Enumerable.Repeat("int item;\n",1000)):new string(' ',30000));
        for(int i=0;i<3;i++)Lexer.Tokenize(source);
        long before=GC.GetAllocatedBytesForCurrentThread();var tokens=Lexer.Tokenize(source);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        long limit=dense?1024*1024:64*1024;
        Assert.True(allocated<limit,$"Lexer allocated {allocated:N0} bytes; fixture budget {limit:N0}");
        Assert.Equal(dense?3001:1,tokens.Count);
        Assert.All(tokens,t=>Assert.Same(source,t.Source));
        if(dense){Assert.Equal("item",tokens[1].Text);Assert.Equal(source.Span(4,4),tokens[1].Span);}
    }
}
