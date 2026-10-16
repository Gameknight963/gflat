using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    public NamedTypeExpression? GetGenericTypeOrigin(string identity) => extensionGenericTypeOrigins.GetValueOrDefault(identity);
    public bool IsResolvedTypeIdentity(TypeExpression type) => type is NamedTypeExpression { IsResolvedNominal: true };
}
