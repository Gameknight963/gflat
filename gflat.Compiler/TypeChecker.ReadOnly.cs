using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private bool IsDeepReceiverType(TypeExpression type) => ResolveAlias(type) switch
    {
        PointerTypeExpression p => p.IsImmutable,
        ManagedTypeExpression m => m.IsImmutable,
        var value => value.IsImmutableValue
    };

    private bool IsReceiverImmutable(AstNode expression) => IsDeepReceiverType(GetType(expression)) ||
        (ResolveAlias(GetType(expression)) is not PointerTypeExpression and not ManagedTypeExpression && IsExpressionImmutable(expression));

    private bool IsExpressionImmutable(AstNode node)
    {
        if (ResolveAlias(GetType(node)).IsImmutableValue) return true;
        if (_propertyReads.ContainsKey(node)) return false;
        if (node is IdentifierExpression id)
        {
            bool field = id.Name == "this" || !_scopes.Any(s => s.ContainsKey(id.Name)) &&
                (_currentStruct?.FieldIndex(id.Name) >= 0 || _currentClass?.FieldIndex(id.Name) >= 0);
            return field && TryLookupVariable("this", out var owner) && owner != null && IsDeepReceiverType(owner);
        }
        if (node is MemberAccessExpression member) return IsReceiverImmutable(member.Object);
        if (node is UnaryExpression { Operator: TokenKind.Star } dereference) return IsDeepReceiverType(GetType(dereference.Operand));
        if (node is IndexExpression index)
        {
            var target = ResolveAlias(GetType(index.Target));
            return target is PointerTypeExpression or ManagedTypeExpression ? IsDeepReceiverType(target) : IsExpressionImmutable(index.Target);
        }
        return false;
    }

    private void CheckReceiverCapability(MethodDeclaration method, bool readOnly, bool immutable, int line)
    {
        if (immutable && !method.IsImmutable)
            throw new gflat.CompileExceptions.TypeCheckException($"Cannot call non-immutable {DescribeSourceCallable(method)} on immutable instance", line);
        if (readOnly && !method.IsReadOnly)
            throw new gflat.CompileExceptions.TypeCheckException($"Cannot call non-readonly {DescribeSourceCallable(method)} on readonly instance", line);
    }

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
        if (!type.IsImmutableValue) return true;
        if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression) return true;
        if (type is ArrayTypeExpression a) return CanCopyReadOnlyElement(TypeQualifiers.Immutable(a.ElementType), visiting);
        if (type is not NamedTypeExpression n) return true;
        // An explicit copy constructor reads the readonly source and defines
        // the new value's resources. Its fields are not copied as raw aliases.
        // ValidateCopyUse separately checks accessibility and throwing behavior.
        if (CopyConstructor(type) is { } copy)
            return ResolveAlias(copy.Parameters[0].Type) is PointerTypeExpression { IsImmutable: true };
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
            PointerTypeExpression p => p.IsReadOnly && TypesMatchPublic(p.Inner, TypeQualifiers.Immutable(p.Inner)),
            ManagedTypeExpression m => m.IsReadOnly && TypesMatchPublic(m.Inner, TypeQualifiers.Immutable(m.Inner)),
            _ => CanCopyReadOnlyValue(TypeQualifiers.Immutable(type), visiting)
        };
    }

    private bool CanReadOnlyView(TypeExpression target, TypeExpression source)
    {
        target = ResolveAlias(target);
        source = ResolveAlias(source);
        if (source.IsImmutableValue && !target.IsImmutableValue) return false;
        return target.IsImmutableValue
            ? TypesMatchPublic(TypeQualifiers.Immutable(target), TypeQualifiers.Immutable(source))
            : TypesMatchPublic(TypeQualifiers.ReadOnly(target), TypeQualifiers.ReadOnly(source));
    }
}
