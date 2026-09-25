using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private bool CanInitializeReadOnlyField(AstNode target)
    {
        if (_currentConstructor == null || _lambdaScopeBoundaries.Count != 0) return false;
        string? name = target switch
        {
            IdentifierExpression id when !_scopes.Any(s => s.ContainsKey(id.Name)) => id.Name,
            MemberAccessExpression { Object: IdentifierExpression { Name: "this" } } m => m.Member,
            _ => null
        };
        return name != null && (_currentStruct?.FieldDeclarationsByName.ContainsKey(name) == true ||
            _currentClass?.FieldDeclarations.Any(f => f.Name == name) == true);
    }

    // Copying scalar values is safe. Copying an aggregate must not expose mutable
    // aliases to storage reached through its readonly view.
    private bool CanCopyReadOnlyValue(TypeExpression type, HashSet<string>? visiting = null)
    {
        type = ResolveAlias(type);
        if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression) return true;
        if (type is ArrayTypeExpression a) return CanCopyReadOnlyElement(a.ElementType, visiting);
        if (type is not NamedTypeExpression n) return true;
        visiting ??= new();
        if (!visiting.Add(n.Name)) return true;
        IEnumerable<TypeExpression> fields = _structs.TryGetValue(n.Name, out var s) ? s.Fields.Select(f => f.Type) :
            _classes.TryGetValue(n.Name, out var c) ? c.Fields.Select(f => f.Type) : [];
        return fields.All(f => CanCopyReadOnlyElement(f, visiting));
    }

    private bool CanCopyReadOnlyElement(TypeExpression type, HashSet<string>? visiting)
    {
        type = ResolveAlias(type);
        return type switch
        {
            PointerTypeExpression p => p.IsReadOnly && TypesMatchPublic(p.Inner, TypeQualifiers.ReadOnly(p.Inner)),
            ManagedTypeExpression m => m.IsReadOnly && TypesMatchPublic(m.Inner, TypeQualifiers.ReadOnly(m.Inner)),
            _ => CanCopyReadOnlyValue(type, visiting)
        };
    }

    private bool CanReadOnlyView(TypeExpression target, TypeExpression source) =>
        TypesMatchPublic(TypeQualifiers.ReadOnly(ResolveAlias(target)), TypeQualifiers.ReadOnly(ResolveAlias(source)));
}
