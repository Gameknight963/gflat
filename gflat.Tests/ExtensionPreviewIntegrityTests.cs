using gflat.ast;

namespace gflat.Tests;

public sealed class ExtensionPreviewIntegrityTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("Entry")]
    public void SignaturePreviewPreservesBorrowedReceiverArgumentSpans(string argumentName)
    {
        string text="struct Entry{}struct Box<T>{}void Second<T>(this ref Box<T> target){}void Use(Box<"+argumentName+">* target){}";
        var source=new SourceFile("extension-preview-integrity.gf",text);
        var (ast,checker)=Compiler.Check(source,options:new(){NoStdLib=true});
        int members=ast.Members.Count;
        int offset=text.LastIndexOf(argumentName+">",StringComparison.Ordinal);
        var argument=new NamedTypeExpression(argumentName,null,1){Span=source.Span(offset,argumentName.Length)};
        var original=argument.Span;
        var receiver=new NamedTypeExpression("Box",null,1,[argument]);
        var candidate=Assert.Single(checker.GetVisibleExtensions("",[],receiver,true,false,memberName:"Second"));
        Assert.Equal("Second",candidate.Definition.Name);
        Assert.Equal(argumentName,Assert.IsType<NamedTypeExpression>(candidate.GenericBindings["T"]).Name);
        Assert.Equal(members,ast.Members.Count);
        Assert.Equal(original,argument.Span);
    }
}
