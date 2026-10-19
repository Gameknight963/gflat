using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private bool CanInitializeReadOnlyField(AstNode target)
    {
        if (_lambdaScopeBoundaries.Count != 0) return false;
        if (CanWriteOwnPropertyBackingStorage(target)) return true;
        if (_currentFunction is MethodDeclaration { StaticConstructorName: not null } && GetStaticStorage(target) is { IsConst: false } field)
            return GetStaticStorageInfo(field).Owner == (_currentClass?.Name ?? _currentStruct?.Name);
        if (GetStaticStorage(target) != null) return false;
        if (_currentConstructor == null) return false;
        string? name = target switch
        {
            IdentifierExpression id when !_scopes.Any(s => s.ContainsKey(id.Name)) => id.Name,
            MemberAccessExpression { Object: IdentifierExpression { Name: "this" } } m => m.Member,
            _ => null
        };
        return name != null && (_currentStruct?.FieldDeclarationsByName.ContainsKey(name) == true ||
            _currentClass?.FieldDeclarations.Any(f => f.Name == name) == true);
    }

    private bool CanWriteOwnPropertyBackingStorage(AstNode target)
    {
        if (_currentFunction is not MethodDeclaration { PropertyName: not null, IsReadOnly: false } accessor ||
            target is not IdentifierExpression identifier || _scopes.Any(scope => scope.ContainsKey(identifier.Name))) return false;

        FieldDeclaration? field;
        if (_currentClass is { } ownerClass && ownerClass.AllMethods.Contains(accessor))
            field = ownerClass.FieldDeclarations.FirstOrDefault(candidate => candidate.Name == identifier.Name);
        else if (_currentStruct is { } ownerStruct && ownerStruct.AllMethods.Contains(accessor))
            field = ownerStruct.FieldDeclarations.FirstOrDefault(candidate => candidate.Name == identifier.Name);
        else if (_currentClass == null && _currentStruct == null && GetStaticStorage(target) is { } global &&
            GetStaticStorageInfo(global).Owner == null && DeclarationNamespace(accessor) is { } declarationNamespace &&
            DeclarationNamespace(global) == declarationNamespace)
            field = global;
        else return false;

        // Only the lowering-generated field belonging to this exact accessor
        // may be written. Its result qualifiers and pointee views stay intact.
        return field != null && !field.IsConst && field.PropertyName == accessor.PropertyName &&
            field.IsStatic == accessor.IsStatic && (!field.IsStatic || ReferenceEquals(GetStaticStorage(target), field));
    }

    // Copying scalar values is safe. Copying an aggregate must not expose mutable
    // aliases to storage reached through its readonly view.
    private bool CanCopyReadOnlyValue(TypeExpression type, HashSet<string>? visiting = null)
    {
        type = ResolveAlias(type);
        if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression) return true;
        if (type is ArrayTypeExpression a) return CanCopyReadOnlyElement(a.ElementType, visiting);
        if (type is not NamedTypeExpression n) return true;
        // An explicit copy constructor reads the readonly source and defines
        // the new value's resources. Its fields are not copied as raw aliases.
        // ValidateCopyUse separately checks accessibility and throwing behavior.
        if (CopyConstructor(type) != null) return true;
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
