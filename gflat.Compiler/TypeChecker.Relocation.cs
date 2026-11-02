using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    public ConstructorDeclaration? MoveConstructor(TypeExpression type)
    {
        type = ResolveAlias(type);
        if (type is not NamedTypeExpression named) return null;
        return (GetStructByIdentity(named.Name)?.Constructors ?? GetClassByIdentity(named.Name)?.Constructors)
            ?.FirstOrDefault(constructor => constructor.IsMove);
    }

    public bool RequiresRelocation(TypeExpression type) => RequiresRelocation(type, new());

    private bool RequiresRelocation(TypeExpression type, HashSet<string> visited)
    {
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression array) return array.Size != 0 && RequiresRelocation(array.ElementType, visited);
        if (type is not NamedTypeExpression named || !visited.Add(named.Name)) return false;
        if (MoveConstructor(type) != null) return true;
        var cls = GetClassByIdentity(named.Name);
        if (cls?.BaseClass is string parent && RequiresRelocation(NamedTypeExpression.Resolved(parent, type.Line), visited)) return true;
        var fields = GetStructByIdentity(named.Name)?.Fields.Select(field => field.Type) ?? cls?.Fields.Select(field => field.Type);
        return fields?.Any(field => RequiresRelocation(field, visited)) == true;
    }
}
