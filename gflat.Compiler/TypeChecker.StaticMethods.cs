using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<MethodDeclaration, (string Owner, bool IsClass)> specializedMethodOwners = new();
    public bool TryGetMethodOwner(MethodDeclaration method, out string owner, out bool isClass)
    {
        if (specializedMethodOwners.TryGetValue(method, out var specialized))
        { owner = specialized.Owner; isClass = specialized.IsClass; return true; }
        foreach (ClassInfo info in _classes.Values)
            if (info.AllMethods.Contains(method)) { owner = info.Name; isClass = true; return true; }
        foreach (StructInfo info in _structs.Values)
            if (info.AllMethods.Contains(method)) { owner = info.Name; isClass = false; return true; }
        owner = "";
        isClass = false;
        return false;
    }

    private MethodDeclaration? ResolveStaticMethod(NamespaceAccessExpression access, CallExpression? call = null,
        bool deferSelection = false, MethodDeclaration? selectedValue = null)
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
            cls = GetClassByIdentity(resolvedOwner.Name);
            str = GetStructByIdentity(resolvedOwner.Name);
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
        // A specialization body can inspect a later method before its own
        // declaration is checked. Bind the whole family before selecting a call
        // or exposing its function address to the caller's namespace.
        if (overloadFamilies.TryGetValue(method, out var family))
        {
            foreach (var candidate in family.ToArray()) BindMethodSignature(candidate);
        }
        else BindMethodSignature(method);
        if (selectedValue != null) method = selectedValue;
        if (call != null) method = SelectOverload(method, call);
        if (deferSelection && overloadFamilies.ContainsKey(method)) return method;
        ValidateAliasMember(access, method);
        if (!method.IsStatic)
            throw new TypeCheckException($"Instance member '{access.Member}' must be accessed with '.', not '::'", access.Line);
        if (method.Accessibility == TokenKind.Private && !CanAccessPrivate(_currentClass?.Name ?? _currentStruct?.Name, owner!))
            throw new TypeCheckException($"Cannot access private method '{access.Member}'", access.Line);
        string? current = _currentClass?.Name ?? _currentStruct?.Name;
        if (method.Accessibility == TokenKind.Protected && (current == null || !IsSubclassOf(current, owner!)))
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
