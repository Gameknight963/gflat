namespace gflat.ast;

public static class TypeQualifiers
{
    // Protect only storage belonging inline to this value. Indirection is a boundary.
    public static TypeExpression ReadOnly(TypeExpression type)
    {
        if (type.IsReadOnlyValue) return type;
        TypeExpression qualified = type is ArrayTypeExpression a
            ? new ArrayTypeExpression(ReadOnly(a.ElementType), a.Size, a.Line, a.SizeExpression) : type;
        qualified = qualified.WithReadOnlyValue(true);
        qualified.Span = type.Span;
        return qualified;
    }

    // A deep view follows indirections. Primitive leaves have no reachable mutable storage,
    // so their shallow and deep views are the same type.
    public static TypeExpression Immutable(TypeExpression type)
    {
        if (type.IsImmutableValue) return type;
        if (type is NamedTypeExpression n && TypeChecker.IsPrimitive(n.Name)) return ReadOnly(type);
        TypeExpression qualified = type switch
        {
            PointerTypeExpression p => new PointerTypeExpression(Immutable(p.Inner), p.IsNullable, p.Line),
            ManagedTypeExpression m => new ManagedTypeExpression(Immutable(m.Inner), m.IsNullable, m.Line, collector: m.Collector) { UsesDefaultCollector = m.UsesDefaultCollector },
            ArrayTypeExpression a => new ArrayTypeExpression(Immutable(a.ElementType), a.Size, a.Line, a.SizeExpression),
            _ => type
        };
        qualified = qualified.WithImmutableValue(true);
        qualified.Span = type.Span;
        return qualified;
    }
}
