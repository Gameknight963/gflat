namespace gflat.ast;

public static class TypeQualifiers
{
    public static TypeExpression ReadOnly(TypeExpression type)
    {
        if (type.IsReadOnlyValue) return type;
        TypeExpression qualified = type switch
        {
            PointerTypeExpression p => new PointerTypeExpression(p.Inner, p.IsNullable, p.Line, true),
            ManagedTypeExpression m => new ManagedTypeExpression(m.Inner, m.IsNullable, m.Line, true),
            ArrayTypeExpression a => new ArrayTypeExpression(ReadOnly(a.ElementType), a.Size, a.Line, a.SizeExpression),
            _ => type
        };
        qualified = qualified.WithReadOnlyValue(true);
        qualified.Span = type.Span;
        return qualified;
    }
}
