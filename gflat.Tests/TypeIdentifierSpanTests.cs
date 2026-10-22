using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class TypeIdentifierSpanTests
{
    [Fact]
    public void ParserKeepsEachQualifiedGenericIdentifierSpanSeparate()
    {
        const string text = "void Use(immutable(Library::Family<Other::Arg>::Item)*? value) {}";
        var source = new SourceFile("type-spans.gf", text);
        var unit = new Compilation([source], new DiagnosticBag()).Unit;
        var method = unit.Members.OfType<MethodDeclaration>().Single(m => m.Name == "Use");
        var pointer = Assert.IsType<PointerTypeExpression>(method.Parameters.Single().Type);
        var item = Assert.IsType<NestedTypeExpression>(pointer.Inner);
        var family = Assert.IsType<NestedTypeExpression>(item.Parent);
        var library = Assert.IsType<NamedTypeExpression>(family.Parent);
        var arg = Assert.IsType<NestedTypeExpression>(family.TypeArguments.Single());
        var other = Assert.IsType<NamedTypeExpression>(arg.Parent);
        foreach (var (name, span) in new[] { ("Library", library.NameSpan), ("Family", family.NameSpan),
                     ("Other", other.NameSpan), ("Arg", arg.NameSpan), ("Item", item.NameSpan) })
        {
            Assert.Equal(text.IndexOf(name, StringComparison.Ordinal), span.Start);
            Assert.Equal(name.Length, span.Length);
            Assert.Same(source, span.Source);
        }
        var clone = Assert.IsType<NestedTypeExpression>(new AstCloner(new()).CloneType(item));
        Assert.Equal(item.NameSpan, clone.NameSpan);
        Assert.Equal(family.NameSpan, Assert.IsType<NestedTypeExpression>(clone.Parent).NameSpan);
    }

    [Fact]
    public void SubstitutionDoesNotStampTheParametersIdentifierOntoItsArgument()
    {
        var argumentSource = new SourceFile("argument.gf", "Argument");
        var parameterSource = new SourceFile("parameter.gf", "T");
        var argument = new NamedTypeExpression("Argument", null, 1) { NameSpan = argumentSource.Span(0, 8) };
        var parameter = new NamedTypeExpression("T", null, 1) { NameSpan = parameterSource.Span(0, 1) };
        var clone = Assert.IsType<NamedTypeExpression>(new AstCloner(new() { ["T"] = argument }).CloneType(parameter));
        Assert.Same(argument, clone);
        Assert.Equal(argumentSource.Span(0, 8), clone.NameSpan);
    }
}
