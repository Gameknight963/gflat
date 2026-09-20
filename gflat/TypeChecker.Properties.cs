using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<AstNode, CallExpression> _propertyReads = new();
    private readonly Dictionary<AstNode, PropertyWrite> _propertyWrites = new();
    private int _propertyTemporary;
    private int _propertyBackingField;
    private bool _hasProperties;
    public CallExpression? GetPropertyRead(AstNode node) => _propertyReads.GetValueOrDefault(node);
    public PropertyWrite? GetPropertyWrite(AstNode node) => _propertyWrites.GetValueOrDefault(node);

    public sealed record PropertyWrite(AstNode? Receiver, IdentifierExpression? ReceiverTemporary,
        bool ReceiverByAddress, IdentifierExpression? OldTemporary, CallExpression? Getter,
        IdentifierExpression ValueTemporary, AstNode Value, CallExpression Setter, bool Postfix);

    private void ExpandProperties(CompilationUnit unit)
    {
        Walk(unit.Members, null);
        foreach (var ns in unit.Namespaces) Walk(ns.Members, null);
        void Walk(List<AstNode> members, AstNode? owner)
        {
            foreach (AstNode member in members.ToArray())
            {
                if (member is NamespaceDeclaration ns) Walk(ns.Members, null);
                else if (member is ClassDeclaration cls) Walk(cls.Members, cls);
                else if (member is StructDeclaration str) Walk(str.Members, str);
                else if (member is InterfaceDeclaration iface) Walk(iface.Members, iface);
                else if (member is PropertyDeclaration property && !property.Expanded)
                {
                    using var context = SourceContext.Enter(property.Span);
                    _hasProperties = true;
                    if (owner == null) throw new TypeCheckException("Properties must be declared in a class, struct, or interface", property.Line);
                    var accessors = new[] { property.Getter, property.Setter }.OfType<MethodDeclaration>().ToArray();
                    bool signature = owner is InterfaceDeclaration || accessors[0].IsAbstract;
                    bool auto = !signature && accessors.All(a => a.Body == null);
                    if (owner is InterfaceDeclaration && accessors.Select(a => a.Accessibility).Distinct().Count() > 1)
                        throw new TypeCheckException("Interface property accessors cannot have restricted accessibility", property.Line);
                    if (owner is InterfaceDeclaration && accessors.Any(a => a.Throws))
                        throw new TypeCheckException("Throwing interface property accessors are not yet supported", property.Line);
                    if (signature && accessors.Any(a => a.Body != null))
                        throw new TypeCheckException("Abstract and interface properties cannot have accessor bodies", property.Line);
                    if (!signature && !auto && accessors.Any(a => a.Body == null))
                        throw new TypeCheckException("All accessors of a property must either have bodies or be automatic", property.Line);
                    if (owner is StructDeclaration && accessors.Any(a => a.IsVirtual || a.IsOverride || a.IsAbstract))
                        throw new TypeCheckException("Struct properties cannot be virtual, override, or abstract", property.Line);
                    if (accessors.Any(a => a.IsStatic && (a.IsVirtual || a.IsOverride || a.IsAbstract)) || owner is InterfaceDeclaration && accessors.Any(a => a.IsStatic))
                        throw new TypeCheckException("Static properties cannot be virtual, abstract, or interface members", property.Line);
                    if (auto && accessors[0].IsStatic)
                        throw new TypeCheckException("Static auto-properties require static field storage, which is not yet supported; use explicit accessors", property.Line);
                    if (property.Initializer != null && !auto)
                        throw new TypeCheckException("Only auto-properties may have initializers", property.Line);
                    if (auto && property.Getter == null)
                        throw new TypeCheckException("An auto-property requires a getter", property.Line);
                    var additions = new List<AstNode>();
                    string backing = "$property$" + _propertyBackingField++ + "$" + property.Name;
                    if (auto)
                        additions.Add(new FieldDeclaration(backing, property.Type, property.Initializer, TokenKind.Private, false, false, property.Line) { Span = property.Span });
                    foreach (var accessor in accessors)
                    {
                        bool getter = accessor == property.Getter;
                        BlockStatement? body = accessor.Body;
                        if (auto)
                        {
                            AstNode storage = new IdentifierExpression(backing, property.Line);
                            AstNode statement = getter ? new ReturnStatement(storage, property.Line) :
                                new ExpressionStatement(new AssignmentExpression(storage, new IdentifierExpression("value", property.Line), TokenKind.Equals, property.Line), property.Line);
                            body = new BlockStatement(new() { statement }, property.Line);
                        }
                        additions.Add(new MethodDeclaration(accessor.Name, accessor.ReturnType, accessor.Parameters, body,
                            owner is InterfaceDeclaration ? TokenKind.Public : accessor.Accessibility,
                            accessor.IsStatic, accessor.IsVirtual, accessor.IsOverride, signature, accessor.Line,
                            accessor.IsReadOnly || auto && getter && !accessor.IsVirtual && !accessor.IsOverride, throws: accessor.Throws)
                        { PropertyName = property.Name, Span = accessor.Span.Source == null ? property.Span : accessor.Span });
                    }
                    if (auto && property.Setter == null)
                    {
                        var assignment = new AssignmentExpression(new IdentifierExpression(backing, property.Line), new IdentifierExpression("value", property.Line), TokenKind.Equals, property.Line);
                        additions.Add(new MethodDeclaration("$set$" + property.Name, new NamedTypeExpression("void", null, property.Line),
                            new() { new Parameter("value", property.Type, property.Line) },
                            new BlockStatement(new() { new ExpressionStatement(assignment, property.Line) }, property.Line),
                            TokenKind.Private, false, false, false, false, property.Line)
                        { PropertyName = property.Name, IsPropertyInitializer = true, Span = property.Span });
                    }
                    members.InsertRange(members.IndexOf(property) + 1, additions);
                    property.Expanded = true;
                }
            }
        }
    }

    private sealed record PropertyAccess(string Name, string Owner, MethodDeclaration? Getter,
        MethodDeclaration? Setter, AstNode? Receiver, AstNode? StaticOwner);

    private PropertyAccess? FindProperty(AstNode expression)
    {
        if (!_hasProperties) return null;
        string name;
        string? owner;
        AstNode? receiver = null, staticOwner = null;
        switch (expression)
        {
            case IdentifierExpression id:
                if (_scopes.Any(s => s.ContainsKey(id.Name))) return null;
                name = id.Name;
                owner = _currentClass?.Name ?? _currentStruct?.Name;
                break;
            case MemberAccessExpression member:
                if (member.IsArrow) return null;
                // Namespace/type operands must retain the usual '.' versus '::' diagnostics.
                if (member.Object is IdentifierExpression typeId && !TryLookupVariable(typeId.Name, out _) &&
                    (GetClass(typeId.Name) != null || GetStruct(typeId.Name) != null || IsInterface(typeId.Name))) return null;
                if (ResolveNamespace(member.Object) != null || ResolveEnum(member.Object) != null) return null;
                if (!_types.ContainsKey(member.Object)) member.Object.Accept(this);
                TypeExpression type = ResolveAlias(GetType(member.Object));
                type = type switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner, _ => type };
                owner = (ResolveAlias(type) as NamedTypeExpression)?.Name;
                name = member.Member;
                receiver = member.Object;
                break;
            case NamespaceAccessExpression access:
                name = access.Member;
                staticOwner = access.Left;
                if (access.Left is IdentifierExpression typeName)
                {
                    if (TryLookupVariable(typeName.Name, out _)) return null;
                    owner = GetClass(typeName.Name)?.Name ?? GetStruct(typeName.Name)?.Name;
                }
                else if (access.Left is NamespaceAccessExpression qualified && ResolveNamespace(qualified.Left) is NamespaceScope scope)
                    owner = scope.Classes.GetValueOrDefault(qualified.Member)?.Name ?? scope.Structs.GetValueOrDefault(qualified.Member)?.Name;
                else return null;
                break;
            default: return null;
        }
        if (owner == null) return null;
        MethodDeclaration? getter = null, setter = null;
        string declaring = owner;
        if (_classes.TryGetValue(owner, out var cls))
        {
            // A declaration hides the whole property, not just one accessor.
            // Do not accidentally borrow a setter from a different base property.
            for (ClassInfo? current = cls; current != null;
                current = current.BaseClass != null ? _classes.GetValueOrDefault(current.BaseClass) : null)
            {
                var declared = current.AllMethods.Where(m => m.PropertyName == name).ToArray();
                if (declared.Length == 0) continue;
                getter = declared.FirstOrDefault(m => m.Name == "$get$" + name);
                setter = declared.FirstOrDefault(m => m.Name == "$set$" + name);
                declaring = current.Name;
                break;
            }
        }
        else if (_structs.TryGetValue(owner, out var str))
        {
            getter = str.Methods.GetValueOrDefault("$get$" + name);
            setter = str.Methods.GetValueOrDefault("$set$" + name);
        }
        else if (_interfaces.TryGetValue(owner, out var iface))
        {
            getter = iface.MethodsByName.GetValueOrDefault("$get$" + name);
            setter = iface.MethodsByName.GetValueOrDefault("$set$" + name);
        }
        if (getter == null && setter == null) return null;
        var method = getter ?? setter!;
        if (staticOwner != null && !method.IsStatic)
            throw new TypeCheckException($"Instance property '{name}' must be accessed with '.'", expression.Line);
        if (receiver != null && method.IsStatic)
            throw new TypeCheckException($"Static property '{name}' must be accessed with '::'", expression.Line);
        if (expression is IdentifierExpression && !method.IsStatic)
        {
            receiver = new IdentifierExpression("this", expression.Line) { Span = expression.Span };
            receiver.Accept(this);
        }
        return new(name, declaring, getter, setter, receiver, staticOwner);
    }

    private void ValidatePropertyAccessor(MethodDeclaration method)
    {
        if (method.PropertyName == null) return;
        TypeExpression type = ResolveAlias(method.Name.StartsWith("$get$") ? method.ReturnType : method.Parameters[0].Type);
        if (HasDestructor(type) || type is ArrayTypeExpression || type is NamedTypeExpression { Name: "void" })
            throw new TypeCheckException("Properties cannot currently return void, arrays, or values with destructors; use a pointer for owned storage", method.Line);
    }

    private MethodDeclaration RequireAccessor(PropertyAccess property, bool getter, AstNode site)
    {
        var method = getter ? property.Getter : property.Setter;
        if (method == null) throw new TypeCheckException($"Property '{property.Name}' has no {(getter ? "getter" : "setter")}", site.Line);
        if (method.IsPropertyInitializer && !(_currentConstructor != null &&
            (_currentClass?.Name ?? _currentStruct?.Name) == property.Owner && property.Receiver is IdentifierExpression { Name: "this" }))
            throw new TypeCheckException($"Get-only auto-property '{property.Name}' can only be assigned in its declaring constructor", site.Line);
        string? current = _currentClass?.Name ?? _currentStruct?.Name;
        if (method.Accessibility == TokenKind.Private && !CanAccessPrivate(current, property.Owner))
            throw new TypeCheckException($"Cannot access private {(getter ? "getter" : "setter")} of property '{property.Name}'", site.Line);
        if (method.Accessibility == TokenKind.Protected && (_currentClass == null || !IsSubclassOf(_currentClass.Name, property.Owner)))
            throw new TypeCheckException($"Cannot access protected accessor of property '{property.Name}'", site.Line);
        ValidatePropertyAccessor(method);
        return method;
    }

    private CallExpression PropertyCall(PropertyAccess property, MethodDeclaration method, AstNode? receiver, List<AstNode> args, AstNode site)
    {
        AstNode callee = receiver != null ? new MemberAccessExpression(receiver, method.Name, false, site.Line) :
            property.StaticOwner != null ? new NamespaceAccessExpression(property.StaticOwner, method.Name, site.Line) :
            new IdentifierExpression(method.Name, site.Line);
        callee.Span = site.Span;
        var call = new CallExpression(callee, args, site.Line) { Span = site.Span };
        call.Accept(this);
        return call;
    }

    private bool CheckPropertyRead(AstNode node)
    {
        var property = FindProperty(node);
        if (property == null) return false;
        var getter = RequireAccessor(property, true, node);
        var call = PropertyCall(property, getter, property.Receiver, new(), node);
        _propertyReads[node] = call;
        RecordType(node, GetType(call));
        return true;
    }

    private bool CheckPropertyWrite(AstNode node, AstNode target, AstNode value, TokenKind? binary, bool postfix, TokenKind? increment = null)
    {
        var property = FindProperty(target);
        if (property == null) return false;
        var setter = RequireAccessor(property, false, node);
        TypeExpression type = ResolveAlias(setter.Parameters[0].Type);
        RecordType(target, type);
        PushScope();
        try
        {
            IdentifierExpression Temporary(TypeExpression t)
            {
                var temp = new IdentifierExpression("$propertyTemp$" + _propertyTemporary++, node.Line) { Span = node.Span };
                DeclareVariable(temp.Name, t, node.Line);
                temp.Accept(this);
                return temp;
            }
            bool byAddress = false;
            IdentifierExpression? receiver = null;
            if (property.Receiver != null)
            {
                var receiverType = ResolveAlias(GetType(property.Receiver));
                byAddress = receiverType is not (PointerTypeExpression or ManagedTypeExpression) &&
                    !(receiverType is NamedTypeExpression named && IsInterface(named.Name));
                if (byAddress && IsPropertyValueStorage(property.Receiver))
                    throw new TypeCheckException("Cannot modify a value returned by a property", node.Line);
                receiver = Temporary(byAddress ? new PointerTypeExpression(receiverType, false, node.Line, IsExpressionReadOnly(property.Receiver)) : receiverType);
            }
            IdentifierExpression? old = null;
            CallExpression? get = null;
            if (binary != null || increment != null)
            {
                get = PropertyCall(property, RequireAccessor(property, true, node), receiver, new(), node);
                old = Temporary(type);
                value = increment != null
                    ? new UnaryExpression(old, increment.Value, true, node.Line) { Span = node.Span }
                    : new BinaryExpression(old, value, binary!.Value, node.Line) { Span = node.Span };
            }
            value.Accept(this);
            if (!IsAssignable(type, GetType(value), value))
                throw new TypeCheckException($"Cannot assign '{TypeName(GetType(value))}' to property '{property.Name}' of type '{TypeName(type)}'", node.Line);
            var assigned = Temporary(type);
            var call = PropertyCall(property, setter, receiver, new() { assigned }, node);
            _propertyWrites[node] = new(property.Receiver, receiver, byAddress, old, get, assigned, value, call, postfix);
            RecordType(node, type);
            return true;
        }
        finally { PopScope(); }
    }

    private bool IsPropertyValueStorage(AstNode node)
    {
        if (ResolveAlias(GetType(node)) is PointerTypeExpression or ManagedTypeExpression) return false;
        return _propertyReads.ContainsKey(node) || node is MemberAccessExpression member && IsPropertyValueStorage(member.Object);
    }
}
