using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class SourceContextRestorationTests
{
    private sealed class ProbeNode(Action<IVisitor> action):AstNode(0)
    {
        public override void Accept(IVisitor visitor)=>action(visitor);
    }
    private static BlockStatement Block(SourceSpan span,Action<IVisitor> action)
        => new([new ProbeNode(action)],span.Line){Span=span};
    private static SourceSpan Current()=>new TypeCheckException("context probe",0).Span;

    [Fact]
    public void EqualNestedRangesAndDifferentNestedSourcesRestoreTheExactOuterRange()
    {
        var source=new SourceFile("outer.gf","first\n  second\n");var outer=source.Span(8,3);
        var other=new SourceFile("inner.gf","different");var inner=other.Span(2,4);
        var refreshed=new SourceFile(source.Path,source.Text);var refreshedRange=refreshed.Span(8,3);
        Block(outer,visitor=>{
            Assert.Equal(outer,Current());
            Block(outer,nested=>{
                Assert.Equal(outer,Current());
                Block(inner,_=>Assert.Equal(inner,Current())).Accept(nested);
                Assert.Equal(outer,Current());
                Block(refreshedRange,_=>Assert.Same(refreshed,Current().Source)).Accept(nested);
                Assert.Same(source,Current().Source);
                // A source-free range inherits the enclosing context.
                Block(default,_=>Assert.Equal(outer,Current())).Accept(nested);
                Assert.Equal(outer,Current());
            }).Accept(visitor);
            Assert.Equal(outer,Current());
        }).Accept(new TypeChecker());
        Assert.Null(Current().Source);
    }

    [Fact]
    public void ReentrantCompilerAnalysisAndLexerFailureRestoreTheCallingVisitorContext()
    {
        var source=new SourceFile("visitor.gf","first\n  parent");var outer=source.Span(8,4);
        Block(outer,_=>{
            Compiler.Check(new SourceFile("valid.gf","int main()=>0;"),options:new(){NoStdLib=true});
            Assert.Equal(outer,Current());
            var invalid=new SourceFile("invalid.gf","int main()=>missing;");
            Assert.Equal("invalid.gf",Assert.Throws<TypeCheckException>(()=>Compiler.Check(invalid,options:new(){NoStdLib=true})).FilePath);
            Assert.Equal(outer,Current());
            Assert.Equal("lexer.gf",Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize(new SourceFile("lexer.gf","  @"))).FilePath);
            Assert.Equal(outer,Current());
            // A plain string with different contents retains the pre-existing
            // ambient line mapping instead of binding itself to another file.
            Assert.Equal(source.LineSpan(1),Assert.Throws<TypeCheckException>(()=>Lexer.Tokenize("@")).Span);
            Assert.Equal(outer,Current());
        }).Accept(new TypeChecker());
        Assert.Null(Current().Source);
    }

    [Fact]
    public void ExceptionalNestedVisitorExitRestoresOuterAndCallerContexts()
    {
        var source=new SourceFile("exceptions.gf","outer\ninner");var outer=source.Span(0,5);var inner=source.Span(6,5);
        Block(outer,visitor=>{
            Assert.Throws<InvalidOperationException>(()=>Block(inner,_=>throw new InvalidOperationException("test")).Accept(visitor));
            Assert.Equal(outer,Current());
        }).Accept(new TypeChecker());
        Assert.Null(Current().Source);
    }

    [Fact]
    public async Task ChildTasksInheritOuterContextAndKeepTheirNestedChangesIsolated()
    {
        await Task.WhenAll(Enumerable.Range(0,8).Select(index=>Task.Run(()=>{
            var source=new SourceFile($"task-{index}.gf","outer\n inner");var outer=source.Span(0,5);var inner=source.Span(7,3);
            Block(outer,_=>{
                Task.Run(()=>{
                    Assert.Equal(outer,Current());
                    Block(inner,visitor=>Assert.Equal(inner,Current())).Accept(new TypeChecker());
                    Assert.Equal(outer,Current());
                }).GetAwaiter().GetResult();
                Assert.Equal(outer,Current());
            }).Accept(new TypeChecker());
            Assert.Null(Current().Source);
        })));
        Assert.Null(Current().Source);
    }

    [Fact]
    public void RepeatedDeclarationParsingAvoidsAllocatingForIdenticalDiagnosticScopes()
    {
        var source=new SourceFile("parser-allocation.gf",string.Concat(Enumerable.Repeat("int value;\n",3000)));
        var tokens=Lexer.Tokenize(source);for(int i=0;i<3;i++)Parser.Parse(tokens);
        long before=GC.GetAllocatedBytesForCurrentThread();var unit=Parser.Parse(tokens);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        // Baseline: 6.28 MB; equality guards: 4.27 MB on this fixed fixture.
        const long limit=11*1024*1024/2;
        Assert.True(allocated<limit,$"Parser allocated {allocated:N0} bytes; fixture budget {limit:N0}");
        Assert.Equal(3000,unit.Members.Count);
    }
}
