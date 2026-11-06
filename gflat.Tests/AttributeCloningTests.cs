using gflat.ast;

namespace gflat.Tests;

public sealed class AttributeCloningTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void SpecializedAttributesKeepTheirSourceLocationsAndIndependentArguments(string kind)
    {
        var source = new SourceFile("attribute-cloning.gf", "[Tag(7)] " + kind + " Box<T> { [Tag(8)] public T Value; [Tag(9)] public T Read() => Value; }");
        var unit = Parser.Parse(Lexer.Tokenize(source));
        var original = Assert.Single(unit.Members);
        var clone = new AstCloner(new() { ["T"] = new NamedTypeExpression("int", null, 1) }).CloneNode(original);
        static IEnumerable<AstNode> Declarations(AstNode node) => new[] { node }.Concat(node switch
        {
            StructDeclaration declaration => declaration.Members,
            ClassDeclaration declaration => declaration.Members,
            _ => []
        });
        foreach (var pair in Declarations(original).Zip(Declarations(clone)))
        {
            var attribute = Assert.Single(pair.First.Attributes);
            var copied = Assert.Single(pair.Second.Attributes);
            Assert.NotSame(attribute, copied);
            Assert.NotSame(attribute.Arguments, copied.Arguments);
            Assert.Equal(attribute.Span, copied.Span);
            Assert.Equal(attribute.Arguments[0].Span, copied.Arguments[0].Span);
            Assert.NotSame(attribute.Arguments[0], copied.Arguments[0]);
        }
    }
}
