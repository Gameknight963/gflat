using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    // Editor metadata only. Resolution never reads this map, and provisional
    // identities published while checking a specialization are not recorded.
    private readonly Dictionary<TypeExpression, TypeExpression> sourceTypeBindings = new(ReferenceEqualityComparer.Instance);

    public TypeExpression? GetSourceTypeBinding(TypeExpression source) => sourceTypeBindings.GetValueOrDefault(source);

    private void RecordSourceTypeBinding(TypeExpression source, TypeExpression resolved)
    {
        var span = source switch {
            NamedTypeExpression named => named.NameSpan,
            NestedTypeExpression nested => nested.NameSpan,
            _ => default
        };
        if (span.Source == null || span.Length == 0 || resolved is not NamedTypeExpression namedType ||
            GetTypeDeclaration(namedType.Name) == null) return;
        sourceTypeBindings.TryAdd(source, resolved);
    }
}
