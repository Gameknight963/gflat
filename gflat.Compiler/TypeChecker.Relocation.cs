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

    public bool RequiresRelocation(TypeExpression type) => RequiresRelocation(type, null);

    private bool RequiresRelocation(TypeExpression type, HashSet<string>? visited)
    {
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression array) return array.Size != 0 && RequiresRelocation(array.ElementType, visited);
        if (type is not NamedTypeExpression named) return false;
        var structure = GetStructByIdentity(named.Name);
        var cls = structure == null ? GetClassByIdentity(named.Name) : null;
        if (structure == null && cls == null) return false;
        foreach (var constructor in structure?.Constructors ?? cls!.Constructors)
            if (constructor.IsMove) return true;
        if (!(visited ??= new()).Add(named.Name)) return false;
        if (cls?.BaseClass is string parent && RequiresRelocation(NamedTypeExpression.Resolved(parent, type.Line), visited)) return true;
        if (structure != null)
        {
            foreach (var field in structure.Fields)
                if (RequiresRelocation(field.Type, visited)) return true;
        }
        else
        {
            foreach (var field in cls!.Fields)
                if (RequiresRelocation(field.Type, visited)) return true;
        }
        return false;
    }
}
