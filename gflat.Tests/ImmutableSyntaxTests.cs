using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ImmutableSyntaxTests
{
    [Fact]
    public void ReceiversAndAccessorModifiersKeepTheirDepth()
    {
        var diagnostics = new DiagnosticBag();
        var unit = Parser.Parse(Lexer.Tokenize("""
            struct Buffer {
                public immutable int Read() => 0;
                public readonly int Shallow() => 0;
                public int Value { immutable get => 0; readonly set {} }
                public immutable int this[int index] { get => index; }
            }
            """), diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        var buffer = Assert.IsType<StructDeclaration>(Assert.Single(unit.Members));
        var read = buffer.Members.OfType<MethodDeclaration>().Single(m => m.Name == "Read");
        var shallow = buffer.Members.OfType<MethodDeclaration>().Single(m => m.Name == "Shallow");
        Assert.True(read.IsImmutable && read.IsReadOnly);
        Assert.True(shallow.IsReadOnly && !shallow.IsImmutable);
        var property = buffer.Members.OfType<PropertyDeclaration>().Single(p => p.Name == "Value");
        Assert.True(property.Getter!.IsImmutable);
        Assert.False(property.Setter!.IsImmutable);
        Assert.True(property.Setter.IsReadOnly);
        Assert.True(buffer.Members.OfType<PropertyDeclaration>().Single(p => p.Name == "$index").Getter!.IsImmutable);
    }

    [Theory]
    [InlineData("immutable(Buffer)*", true, false)]
    [InlineData("readonly(Buffer)*", false, false)]
    [InlineData("immutable(Buffer*)", true, true)]
    [InlineData("readonly(Buffer*)", false, true)]
    public void TypeWrappersRetainPlacementAndDepth(string type, bool deep, bool protectsPointerStorage)
    {
        var diagnostics = new DiagnosticBag();
        var unit = Parser.Parse(Lexer.Tokenize(type + " value;"), diagnostics);
        Assert.False(diagnostics.HasErrors, diagnostics.FormatAll());
        var pointer = Assert.IsType<PointerTypeExpression>(Assert.IsType<FieldDeclaration>(Assert.Single(unit.Members)).Type);
        Assert.Equal(protectsPointerStorage, pointer.IsReadOnlyValue);
        Assert.Equal(deep, protectsPointerStorage ? pointer.IsImmutableValue : pointer.Inner.IsImmutableValue);
    }

    [Theory]
    [InlineData("readonly immutable int Read()=>0;")]
    [InlineData("immutable readonly int Read()=>0;")]
    [InlineData("int Value{readonly immutable get=>0;}")]
    [InlineData("int Value{immutable readonly get=>0;}")]
    public void CombinedReceiverModifiersAreRejected(string member)
        => Assert.ThrowsAny<Exception>(() => Parser.Parse(Lexer.Tokenize("struct Buffer{" + member + "}")));
}
