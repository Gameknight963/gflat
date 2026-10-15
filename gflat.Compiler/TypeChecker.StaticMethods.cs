using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public bool TryGetMethodOwner(MethodDeclaration method, out string owner, out bool isClass)
    {
        foreach (ClassInfo info in _classes.Values)
            if (info.AllMethods.Contains(method)) { owner = info.Name; isClass = true; return true; }
        foreach (StructInfo info in _structs.Values)
            if (info.AllMethods.Contains(method)) { owner = info.Name; isClass = false; return true; }
        owner = "";
        isClass = false;
        return false;
    }

    private MethodDeclaration? ResolveStaticMethod(NamespaceAccessExpression access, CallExpression? call = null)
    {
        ValidateAliasMember(access);
        ClassInfo? cls = null;
        StructInfo? str = null;
        if (access.Left is IdentifierExpression id)
        {
            cls = GetClass(id.Name);
            str = GetStruct(id.Name);
        }
        else if (access.Left is NamespaceAccessExpression type)
        {
            NamespaceScope? scope = ResolveNamespace(type.Left);
            scope?.Classes.TryGetValue(type.Member, out cls);
            scope?.Structs.TryGetValue(type.Member, out str);
        }
        if (cls == null && str == null && StaticOwnerType(access.Left) is { } ownerType &&
            ResolveAlias(ownerType) is NamedTypeExpression resolvedOwner)
        {
            cls = GetClass(resolvedOwner.Name);
            str = GetStruct(resolvedOwner.Name);
        }
        MethodDeclaration? method = null;
        string? owner = null;
        if (cls != null && cls.Methods.TryGetValue(access.Member, out var entry))
        {
            method = entry.Method;
            owner = entry.DeclaringClass;
        }
        else if (str != null && str.Methods.TryGetValue(access.Member, out method)) owner = str.Name;
        if (method == null) return null;
        if (call != null) method = SelectOverload(method, call);
        if (!method.IsStatic)
            throw new TypeCheckException($"Instance member '{access.Member}' must be accessed with '.', not '::'", access.Line);
        if (method.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name ?? _currentStruct?.Name, owner!))
            throw new TypeCheckException($"Cannot access private method '{access.Member}'", access.Line);
        if (method.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, owner!)))
            throw new TypeCheckException($"Cannot access protected method '{access.Member}'", access.Line);
        return method;
    }

    private static TypeExpression? StaticOwnerType(AstNode expression) => expression switch
    {
        TypeExpression type => type,
        IdentifierExpression id => new NamedTypeExpression(id.Name, null, id.Line) { Span = id.Span },
        NamespaceAccessExpression access when StaticOwnerType(access.Left) is { } parent
            => new NestedTypeExpression(parent, access.Member, [], access.Line) { Span = access.Span },
        _ => null
    };
}
