using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<ReflectExpression, ConstValue.Reflection> reflectionTargets = new();
    private readonly HashSet<MethodDeclaration> checkedConstMethodBodies = new();

    private void EnsureConstMethodBody(MethodDeclaration method)
    {
        if (!method.IsConst || checkedConstMethodBodies.Contains(method)) return;
        using var context = EnterMethodSignatureContext(method);
        method.Accept(this);
    }
    public static NamedTypeExpression ReflectionType(int line) => NamedTypeExpression.Resolved("reflection", line);
    public static bool IsReflectionType(TypeExpression type) => type is NamedTypeExpression { Name: "reflection" };
    public static bool ContainsReflectionValue(TypeExpression type) => IsReflectionType(type) ||
        type is ArrayTypeExpression array && ContainsReflectionValue(array.ElementType) ||
        type is ReferenceTypeExpression reference && ContainsReflectionValue(reference.Inner);

    public bool IsCompileTimeOnly(MethodDeclaration method) => !method.IsGeneric &&
        (RequiresReflectionStaging(method) || ContainsReflectionValue(ResolveAlias(method.ReturnType)) ||
            method.Parameters.Any(parameter => ContainsReflectionValue(ResolveAlias(parameter.Type))));

    private void ValidateReflectionMethodSignature(MethodDeclaration method)
    {
        if (!method.IsConst && (ContainsReflectionValue(ResolveAlias(method.ReturnType)) ||
            method.Parameters.Any(parameter => ContainsReflectionValue(ResolveAlias(parameter.Type)))))
            throw new TypeCheckException("A function with reflection parameters or return type must be const", method.Span);
        if ((method.IsVirtual || method.IsOverride || method.IsAbstract) && !method.IsStatic && IsCompileTimeOnly(method))
            throw new TypeCheckException("Compile-time-only methods cannot use virtual dispatch", method.Span);
    }

    private void ValidateReflectionFieldStorage(FieldDeclaration field)
    {
        if (!field.IsConst && ContainsReflectionValue(ResolveAlias(field.Type)))
            throw new TypeCheckException("reflection storage must be const", field.Span);
    }

    public ConstValue.Reflection ReflectType(TypeExpression type, int line)
    {
        type = ResolveAlias(type);
        return new("type:" + GetTypeMangledName(type), "Type", DisplayTypeName(type), type);
    }

    public bool TryGetReflectionTarget(ReflectExpression node, out ConstValue.Reflection? target)
        => reflectionTargets.TryGetValue(node, out target);

    public void Visit(ConstantExpression node)
    {
        RecordType(node, node.Type);
        _constValues[node] = node.Value;
    }

    public void Visit(ReflectExpression node)
    {
        using var source = SourceContext.Enter(node.Span);
        ConstValue.Reflection? reflected = null;
        if (TryReflectDeclaration(node.Target, out var declaration)) reflected = declaration;
        else if (node.Target is TypeExpression type)
        {
            if (TryReflectQualifiedDeclaration(type, out var qualified)) reflected = qualified;
            else if (type is NestedTypeExpression nested)
            {
                var owner = ResolveAlias(nested.Parent);
                if (owner is NamedTypeExpression ownerName)
                {
                    var str = GetStructByIdentity(ownerName.Name);
                    var cls = GetClassByIdentity(ownerName.Name);
                    var iface = ResolveInterface(ownerName);
                    if (cls != null) EnsureClassMembers(cls);
                    var field = str?.FieldDeclarationsByName.GetValueOrDefault(nested.Member) ?? cls?.FieldDeclarationsByName.GetValueOrDefault(nested.Member);
                    if (field != null) reflected = ReflectField(owner, field);
                    else
                    {
                        var methods = (str?.AllMethods ?? cls?.AllMethods ?? iface?.Methods ?? Enumerable.Empty<MethodDeclaration>()).Where(m => m.Name == nested.Member).ToArray();
                        if (methods.Length > 0) reflected = ReflectMethods(methods, owner, nested.Member);
                    }
                }
            }
            if (reflected != null) { }
            else
            {
                if (type is NamedTypeExpression methodName && !IsPrimitive(methodName.Name) && GetClass(methodName.Name) == null &&
                    GetStruct(methodName.Name) == null && ResolveEnum(methodName) == null && !IsInterface(methodName))
                {
                    var methods = ResolveNamespaceCallables(methodName.Name).Methods.ToArray();
                    if (methods.Length > 0) reflected = ReflectMethods(methods, null, methodName.Name);
                    else throw new TypeCheckException($"Unknown declaration or type '{methodName.Name}'", node.Target.Span);
                }
                else reflected = ReflectType(type, node.Line);
            }
        }
        else if (node.Target is IdentifierExpression identifier)
        {
            {
                var methods = ResolveNamespaceCallables(identifier.Name).Methods.ToArray();
                if (methods.Length > 0) reflected = ReflectMethods(methods, null, identifier.Name);
            }
        }
        if (reflected != null) reflectionTargets[node] = reflected;
        else node.Target.Accept(this);
        RecordType(node, ReflectionType(node.Line));
        if (_constEvaluator.TryEvaluate(node, out var value, out _)) _constValues[node] = value!;
        else if (_currentFunction is not MethodDeclaration { IsConst: true })
            throw new TypeCheckException("A reflected value must be a compile-time constant", node.Span);
    }

    private ConstValue.Reflection ReflectMethods(IReadOnlyList<MethodDeclaration> methods, TypeExpression? owner, string name)
        => ReflectMethodSet(methods, owner, name);

    private ConstValue.Reflection ReflectField(TypeExpression owner, FieldDeclaration field)
    {
        using var context = EnterFieldLayoutContext(((NamedTypeExpression)owner).Name, field.Name);
        return new("field:" + GetTypeMangledName(owner) + ":" + field.Name, "Field", field.Name,
            ReflectionOwnerFieldType(owner, field.Name, ResolveFieldLayoutType(field.Type)), field, owner);
    }

    private static string ReflectionSimpleName(string name)
    {
        int depth = 0, start = 0;
        for (int i = 0; i + 1 < name.Length; i++)
        {
            if (name[i] is '<' or '(' or '[') depth++;
            else if (name[i] is '>' or ')' or ']') depth--;
            else if (depth == 0 && name[i] == ':' && name[i + 1] == ':') start = ++i + 1;
        }
        return name[start..];
    }

    public ConstValue ReflectionMember(ConstValue.Reflection target, string member, int line)
    {
        if (TryReflectDeclarationMember(target, member, line, out var declarationMember)) return declarationMember!;
        if (TryReflectMethodMember(target, member, line, out var methodMember)) return methodMember!;
        ConstValue Text(string value) => new ConstValue.String(value);
        ConstValue Number(int value) => new ConstValue.UInteger((ulong)value);
        ConstValue Flag(bool value) => new ConstValue.Boolean(value);
        var type = target.Type == null ? null : ResolveAlias(target.Type);
        if (member == "Name") return Text(ReflectionSimpleName(target.Name));
        if (member == "QualifiedName") return Text(target.DeclaringType == null ? target.Name : DisplayTypeName(target.DeclaringType) + "::" + target.Name);
        if (member == "Kind") return Text(target.Kind);
        if (member == "IsType") return Flag(target.Kind == "Type");
        if (member == "Type" && type != null) return ReflectType(type, line);
        if (member == "Target" && target.Kind == "Alias" && type != null) return ReflectType(type, line);
        if (member == "DeclaringType" && target.DeclaringType != null) return ReflectType(target.DeclaringType, line);
        if (target.Declaration is FieldDeclaration field)
        {
            if (member == "IsStatic") return Flag(field.IsStatic || field.IsConst);
            if (member == "IsConst") return Flag(field.IsConst);
            if (member == "Accessibility") return Text(field.Accessibility.ToString().ToLowerInvariant());
            if (member == "IsPublic") return Flag(field.Accessibility == TokenKind.Public);
            if (member == "IsPrivate") return Flag(field.Accessibility == TokenKind.Private);
            if (member == "Offset") return Number(ReflectionFieldOffset(target, line));
        }
        if (member == "Overloads" && target.Overloads != null)
            return new ConstValue.Array(target.Overloads.Select(method => (ConstValue)ReflectMethods(new[] { method }, target.DeclaringType, method.Name)).ToList());
        if (target.Kind == "Type" && type != null)
        {
            var named = type as NamedTypeExpression;
            var str = named == null ? null : GetStructByIdentity(named.Name);
            var cls = named == null ? null : GetClassByIdentity(named.Name);
            bool hasLayout = !IsReflectionType(type) && type is not FunctionPointerTypeExpression { IsSignature: true } &&
                named?.Name != "void" && (named == null || !IsInterface(named)) && cls?.IsStatic != true;
            if (member == "HasLayout") return Flag(hasLayout);
            if (member == "IsStruct") return Flag(str != null);
            if (member == "IsClass") return Flag(cls != null);
            if (member == "IsInterface") return Flag(named != null && IsInterface(named));
            if (member == "IsEnum") return Flag(named != null && ResolveEnum(named) != null);
            if (member == "IsPrimitive") return Flag(named != null && IsPrimitive(named.Name));
            if (member == "IsArray") return Flag(type is ArrayTypeExpression);
            if (member == "IsReference") return Flag(type is ReferenceTypeExpression);
            if (member == "IsFunctionPointer") return Flag(type is FunctionPointerTypeExpression { IsSignature: false });
            if (member == "IsFunctionSignature") return Flag(type is FunctionPointerTypeExpression { IsSignature: true });
            if (member == "HasElementType") return Flag(type is PointerTypeExpression or ManagedTypeExpression or ArrayTypeExpression or ReferenceTypeExpression);
            if (member == "Size") return Number(type is ReferenceTypeExpression ? TargetInfo.Default.PointerBytes : hasLayout ? GetTypeSize(type) : 0);
            if (member == "Alignment") return Number(type is ReferenceTypeExpression ? TargetInfo.Default.PointerBytes : hasLayout ? GetTypeAlignment(type) : 0);
            if (member == "IsReadOnly") return Flag(type.IsReadOnlyValue);
            if (member == "IsImmutable") return Flag(type.IsImmutableValue);
            if (member == "IsPointer") return Flag(type is PointerTypeExpression or FunctionPointerTypeExpression { IsSignature: false });
            if (member == "IsManaged") return Flag(type is ManagedTypeExpression);
            if (member == "IsNullable") return Flag(type is PointerTypeExpression { IsNullable: true } or ManagedTypeExpression { IsNullable: true } or FunctionPointerTypeExpression { IsNullable: true });
            if (member == "HasDestructor") return Flag(hasLayout && HasDestructor(type));
            if (member == "HasFixedLength") return Flag(type is ArrayTypeExpression { Size: not null });
            if (member == "Length" && type is ArrayTypeExpression { Size: int length }) return Number(length);
            if (member == "ElementType")
            {
                TypeExpression? element = type switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner,
                    ArrayTypeExpression a => a.ElementType, ReferenceTypeExpression r => r.Inner, _ => null };
                if (element != null) return ReflectType(element, line);
            }
            if (member == "CollectorType" && type is ManagedTypeExpression managedType)
                return ReflectType(ResolveCollectorType(managedType.Collector, line), line);
            if (member is "Fields" or "StaticFields")
            {
                IEnumerable<FieldDeclaration> fields = str?.FieldDeclarations ?? cls?.FieldDeclarations ?? Enumerable.Empty<FieldDeclaration>();
                // Class layout contains inherited fields; enumeration describes this declaration.
                var declarations = (str?.Declaration?.Members ?? cls?.Declaration?.Members)?.OfType<FieldDeclaration>();
                if (declarations != null) fields = declarations;
                return new ConstValue.Array(fields.Where(f => f.PropertyName == null && (f.IsStatic || f.IsConst) == (member == "StaticFields"))
                    .Select(f => (ConstValue)ReflectField(type, f)).ToList());
            }
            if (member == "Methods")
            {
                IEnumerable<MethodDeclaration> methods = str?.AllMethods ?? cls?.AllMethods ??
                    (named == null ? null : ResolveInterface(named)?.Methods) ?? Enumerable.Empty<MethodDeclaration>();
                return new ConstValue.Array(methods.Where(m => m.PropertyName == null)
                    .Select(m => (ConstValue)ReflectMethods(new[] { m }, type, m.Name)).ToList());
            }
            if (member == "Bases") return new ConstValue.Array(cls?.BaseClass == null ? new() :
                new() { ReflectType(NamedTypeExpression.Resolved(cls.BaseClass, line), line) });
            if (type is FunctionPointerTypeExpression function)
            {
                if (member == "ReturnType") return ReflectType(function.ReturnType, line);
                if (member == "ParameterTypes") return new ConstValue.Array(function.ParameterTypes.Select(p => (ConstValue)ReflectType(p, line)).ToList());
                if (member == "ParameterCount") return Number(function.ParameterTypes.Count);
            }
        }
        if (target.Declaration is MethodDeclaration methodDecl)
        {
            if (member == "Accessibility") return Text(methodDecl.Accessibility.ToString().ToLowerInvariant());
            if (member == "IsPublic") return Flag(methodDecl.Accessibility == TokenKind.Public);
            if (member == "IsPrivate") return Flag(methodDecl.Accessibility == TokenKind.Private);
            if (member == "IsConst") return Flag(methodDecl.IsConst);
            if (member == "IsReadOnly") return Flag(methodDecl.IsReadOnly);
            if (member == "IsImmutable") return Flag(methodDecl.IsImmutable);
            if (member == "IsNoThrow") return Flag(methodDecl.IsNoThrow);
            if (member == "IsVirtual") return Flag(methodDecl.IsVirtual);
            if (member == "IsOverride") return Flag(methodDecl.IsOverride);
            if (member == "IsAbstract") return Flag(methodDecl.IsAbstract);
            if (member == "ReturnType") return ReflectType(methodDecl.ReturnType, line);
            if (member == "IsStatic") return Flag(methodDecl.IsStatic);
            if (member == "ParameterCount") return Number(methodDecl.Parameters.Count);
            if (member == "ParameterTypes") return new ConstValue.Array(methodDecl.Parameters.Select(p => (ConstValue)ReflectType(p.Type, line)).ToList());
            if (member == "Parameters") return new ConstValue.Array(methodDecl.Parameters.Select((p, i) =>
                (ConstValue)new ConstValue.Reflection(target.Identity + ":parameter:" + i, "Parameter", p.Name,
                    ResolveAlias(p.Type), p, target.DeclaringType)).ToList());
        }
        if (target.Declaration is Parameter parameter)
        {
            if (member == "IsConst") return Flag(parameter.IsConst);
            if (member == "IsRef") return Flag(ResolveAlias(parameter.Type) is ReferenceTypeExpression);
            if (member == "HasDefaultValue") return Flag(parameter.DefaultValue != null);
        }
        throw new ConstEvalException($"Reflection of {target.Kind.ToLowerInvariant()} '{target.Name}' has no applicable member '{member}'", line);
    }

    private int ReflectionFieldOffset(ConstValue.Reflection field, int line)
    {
        if (field.Declaration is not FieldDeclaration { IsStatic: false, IsConst: false } || field.DeclaringType is not NamedTypeExpression owner)
            throw new ConstEvalException("Only instance fields have storage offsets", line);
        IEnumerable<(string Name, TypeExpression Type)> fields;
        int offset = 0;
        if (GetClassByIdentity(owner.Name) is { } cls)
        {
            offset = TargetInfo.Default.PointerBytes;
            fields = cls.Fields.Select(f => (f.Name, f.Type));
        }
        else fields = GetStructByIdentity(owner.Name)!.Fields;
        foreach (var item in fields)
        {
            using var context = EnterFieldLayoutContext(owner.Name, item.Name);
            var itemType = ResolveFieldLayoutType(item.Type);
            int alignment = GetTypeAlignment(itemType);
            offset = checked((offset + alignment - 1) & ~(alignment - 1));
            if (item.Name == field.Name) return offset;
            offset = checked(offset + GetTypeSize(itemType));
        }
        throw new ConstEvalException($"Field '{field.Name}' has no storage offset", line);
    }

    private bool CheckReflectionMember(MemberAccessExpression node)
    {
        if (!IsReflectionType(GetType(node.Object))) return false;
        TypeExpression resultType = node.Member switch {
            "Name" or "QualifiedName" or "Kind" or "Accessibility" => new PointerTypeExpression(new NamedTypeExpression("char", null, node.Line), false, node.Line, isReadOnly: true),
            "Fields" or "StaticFields" or "Methods" or "Bases" or "Overloads" or "ParameterTypes" or "Parameters" or "Constraints" => new ArrayTypeExpression(ReflectionType(node.Line), (int?)null, node.Line),
            "Type" or "DeclaringType" or "ElementType" or "ReturnType" or "Target" or "CollectorType" => ReflectionType(node.Line),
            "Size" or "Alignment" or "Offset" or "Length" or "ParameterCount" or "Position" => new NamedTypeExpression("nuint", null, node.Line),
            "IsType" or "IsStruct" or "IsClass" or "IsInterface" or "IsEnum" or "IsPrimitive" or
            "IsArray" or "IsReference" or "IsFunctionPointer" or "IsFunctionSignature" or "IsReadOnly" or
            "IsImmutable" or "IsPointer" or "IsManaged" or "IsNullable" or "IsStatic" or "IsConst" or
            "IsPublic" or "IsPrivate" or "IsRef" or "IsConstrained" or "IsNoThrow" or "IsVirtual" or "IsOverride" or "IsAbstract" or "HasLayout" or "HasElementType" or
            "HasDestructor" or "HasFixedLength" or "HasDefaultValue" => new NamedTypeExpression("bool", null, node.Line),
            _ => throw new TypeCheckException($"Unknown reflection member '{node.Member}'", node.Span)
        };
        RecordType(node, resultType);
        if (_constEvaluator.TryEvaluate(node, out var value, out var error))
        {
            _constValues[node] = value!;
            if (value is ConstValue.Array array) RecordType(node, new ArrayTypeExpression(ReflectionType(node.Line), array.Elements.Count, node.Line));
        }
        else if (_currentFunction is not MethodDeclaration { IsConst: true }) throw new TypeCheckException(error ?? "Reflection query requires a constant", node.Span);
        return true;
    }

    private bool CheckReflectionSequenceMember(MemberAccessExpression node, TypeExpression type)
    {
        if (type is not ArrayTypeExpression array || !IsReflectionType(array.ElementType) || node.Member is not ("Length" or "length")) return false;
        RecordType(node, new NamedTypeExpression("nuint", null, node.Line));
        if (_constEvaluator.TryEvaluate(node.Object, out var value, out _) && value is ConstValue.Array sequence)
            _constValues[node] = new ConstValue.UInteger((ulong)sequence.Elements.Count);
        return true;
    }
}
