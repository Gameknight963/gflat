using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record NameBinding(TypeExpression? Type = null, NamespaceScope? Namespace = null, bool IsMethodGroup = false);

    private void ValidateNameofSyntax(AstNode node)
    {
        switch (node)
        {
            case IdentifierExpression or NamedTypeExpression or GlobalExpression: return;
            case MemberAccessExpression member when !member.IsArrow: ValidateNameofSyntax(member.Object); return;
            case NamespaceAccessExpression member: ValidateNameofSyntax(member.Left); return;
            default: throw new TypeCheckException("nameof requires a named entity, not a runtime expression", node.Span);
        }
    }

    private NameBinding BindNameof(AstNode node, IReadOnlyDictionary<string, TypeExpression> typeBindings)
    {
        NameBinding Bound(TypeExpression type) { RecordType(node, type); return new(type); }
        if (node is GlobalExpression) return new(Namespace: _globalScope);
        if (node is IdentifierExpression or NamedTypeExpression)
        {
            string name = node is IdentifierExpression identifier ? identifier.Name : ((NamedTypeExpression)node).Name;
            if (TryLookupVariable(name, out var variable)) return Bound(variable!);
            if (typeBindings.TryGetValue(name, out var substitution)) return Bound(substitution);
            var named = node as NamedTypeExpression ?? new NamedTypeExpression(name, null, node.Line);
            var resolved = ResolveAlias(named);
            if (IsPrimitive(name) || resolved is not NamedTypeExpression ||
                resolved is NamedTypeExpression identity && (IsPrimitive(identity.Name) || _classes.ContainsKey(identity.Name) || _structs.ContainsKey(identity.Name) ||
                    _interfaces.ContainsKey(identity.Name) || ResolveEnum(identity) != null)) return Bound(resolved);
            if (ResolveNamespace(node) is { } space) return new(Namespace: space);
            var callables = ResolveNamespaceCallables(name);
            var methods = callables.Methods;
            if (methods.Length != 0)
            {
                var method = methods.FirstOrDefault(candidate => candidate.Accessibility != TokenKind.Private || candidate.Span.SourceId == node.Span.SourceId) ?? methods[0];
                CheckNameofSourceAccess(method, method.Accessibility, node);
                // Naming a group does not bind a signature or an open return T.
                return Bound(callables.IsMerged || method.IsGeneric ? Void : method.ReturnType) with { IsMethodGroup = true };
            }
            if (ResolveExtern(name) is { } external) { ValidateExternAccess(external, node.Span); return Bound(external.ReturnType) with { IsMethodGroup = true }; }
            if (ResolveGlobalField(name) is { } field) { CheckNameofSourceAccess(field, field.Accessibility, node); return Bound(field.Type); }
            // Namespace properties are named even when they have no getter.
            for (var scope = _currentNamespace; scope != null; scope = scope.Parent)
                if (NameofNamespaceMember(scope, name, node) is { } property) return property;
            throw new TypeCheckException($"The name '{name}' does not exist in the current context", node.Span);
        }
        var receiver = node is MemberAccessExpression dot ? dot.Object : ((NamespaceAccessExpression)node).Left;
        string member = node is MemberAccessExpression access ? access.Member : ((NamespaceAccessExpression)node).Member;
        var parent = BindNameof(receiver, typeBindings);
        if (parent.IsMethodGroup) throw new TypeCheckException("A method group cannot be the receiver of a nameof member", receiver.Span);
        if (parent.Namespace is { } ns)
        {
            if (ns.Children.TryGetValue(member, out var child)) return new(Namespace: child);
            if (ns.Aliases.TryGetValue(member, out var alias)) {
                var saved = _currentNamespace;
                try { _currentNamespace = ns; return Bound(ResolveAlias(alias)); }
                finally { _currentNamespace = saved; }
            }
            if (ns.TypeNames.TryGetValue(member, out var identity)) return Bound(NamedTypeExpression.Resolved(identity, node.Line));
            if (NameofNamespaceMember(ns, member, node) is { } entry) return entry;
            throw new TypeCheckException($"Name '{member}' not found in namespace", node.Span);
        }
        var type = ResolveAlias(parent.Type!);
        if (type is ReferenceTypeExpression reference) type = ResolveAlias(reference.Inner);
        if (type is PointerTypeExpression pointer) type = ResolveAlias(pointer.Inner);
        if (type is ManagedTypeExpression managed) type = ResolveAlias(managed.Inner);
        if (type is not NamedTypeExpression owner) throw new TypeCheckException("nameof member requires a named type", node.Span);
        if (ResolveEnum(owner) is { } enumeration && enumeration.Members.ContainsKey(member)) return Bound(type);
        // Nested type names use the same source identity lookup as ordinary types.
        var nested = ResolveAlias(new NestedTypeExpression(owner, member, null, node.Line));
        if (nested is NamedTypeExpression nestedName && (_classes.ContainsKey(nestedName.Name) || _structs.ContainsKey(nestedName.Name) || _interfaces.ContainsKey(nestedName.Name))) return Bound(nested);
        if (_classes.TryGetValue(owner.Name, out var cls))
        {
            int index = cls.FieldIndex(member);
            if (index >= 0) {
                var field = cls.Fields[index]; CheckNameofAccess(field.Accessibility, field.DeclaringClass, member, node);
                return Bound(field.Type);
            }
            for (ClassInfo? current = cls; current != null; current = current.BaseClass == null ? null : _classes.GetValueOrDefault(current.BaseClass))
                if (NameofMethods(current.AllMethods, current.Name, member, node) is { } result) return result;
        }
        if (_structs.TryGetValue(owner.Name, out var str))
        {
            if (str.FieldDeclarationsByName.TryGetValue(member, out var field)) {
                CheckNameofAccess(field.Accessibility, str.Name, member, node); return Bound(field.Type);
            }
            if (NameofMethods(str.AllMethods, str.Name, member, node) is { } result) return result;
        }
        if (ResolveInterface(owner) is { } iface && NameofMethods(iface.Methods, iface.Name, member, node) is { } interfaceMember) return interfaceMember;
        throw new TypeCheckException($"Type '{DisplayTypeName(type)}' has no member '{member}'", node.Span);
    }

    private NameBinding? NameofNamespaceMember(NamespaceScope scope, string name, AstNode node)
    {
        var method = scope.Functions.GetValueOrDefault(name) ?? scope.GenericFunctions.GetValueOrDefault(name) ??
            scope.Functions.GetValueOrDefault("$get$" + name) ?? scope.Functions.GetValueOrDefault("$set$" + name);
        if (method != null) CheckNameofSourceAccess(method, method.Accessibility, node);
        TypeExpression? type = method == null ? null : method.Name.StartsWith("$set$") ? method.Parameters[^1].Type : method.ReturnType;
        if (type == null && scope.Fields.TryGetValue(name, out var field)) { CheckNameofSourceAccess(field, field.Accessibility, node); type = field.Type; }
        if (type == null && scope.Externs.TryGetValue(name, out var external)) { ValidateExternAccess(external, node.Span); type = external.ReturnType; }
        if (type == null) return null;
        RecordType(node, type); return new(type, IsMethodGroup: method?.PropertyName == null && (method != null || scope.Externs.ContainsKey(name)));
    }

    private NameBinding? NameofMethods(IEnumerable<MethodDeclaration> methods, string owner, string name, AstNode node)
    {
        var matches = methods.Where(method => method.Name == name || method.PropertyName == name).ToArray();
        if (matches.Length == 0) return null;
        var selected = matches.FirstOrDefault(method => NameofAccessible(method.Accessibility, owner)) ?? matches[0];
        CheckNameofAccess(selected.Accessibility, owner, name, node);
        var type = selected.Name.StartsWith("$set$") ? selected.Parameters[^1].Type : selected.ReturnType;
        RecordType(node, type); return new(type, IsMethodGroup: selected.PropertyName == null);
    }

    private void CheckNameofSourceAccess(AstNode declaration, TokenKind accessibility, AstNode use)
    {
        if (accessibility == TokenKind.Private && declaration.Span.SourceId != use.Span.SourceId)
            throw new TypeCheckException("Cannot name a private declaration from another source file", use.Span);
    }

    private bool NameofAccessible(TokenKind accessibility, string owner) => accessibility switch {
        TokenKind.Private => CanAccessPrivate(_currentClass?.Name ?? _currentStruct?.Name, owner),
        TokenKind.Protected => _currentClass != null && IsSubclassOf(_currentClass.Name, owner),
        _ => true
    };

    private void CheckNameofAccess(TokenKind accessibility, string owner, string name, AstNode node)
    {
        if (!NameofAccessible(accessibility, owner)) throw new TypeCheckException($"Cannot access {accessibility.ToString().ToLowerInvariant()} member '{name}'", node.Span);
    }
}
