using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<CallExpression, CallExpression> reflectedCalls = new();
    private readonly Dictionary<CallExpression, MethodDeclaration[]> reflectedCallCandidates = new();
    private readonly Dictionary<AstNode, ConstValue.Reflection> reflectedFunctionValues = new();
    private readonly Dictionary<string, ConstValue.Reflection> reflectedParameterTypes = new();

    public CallExpression? GetReflectedCall(CallExpression call) => reflectedCalls.GetValueOrDefault(call);

    private bool TryReflectQualifiedDeclaration(TypeExpression type, out ConstValue.Reflection? reflection)
    {
        reflection = null;
        NamespaceScope scope;
        string member;
        List<TypeExpression> arguments;
        if (type is NamedTypeExpression { Namespace: "global" } rooted)
        {
            scope = _globalScope;
            member = rooted.Name;
            arguments = rooted.TypeArguments;
        }
        else if (type is NestedTypeExpression nested && ReflectionNamespaceQualifier(nested.Parent) is { } qualifier &&
            ResolveNamespace(qualifier) is { } namespaceScope)
        {
            scope = namespaceScope;
            member = nested.Member;
            arguments = nested.TypeArguments;
        }
        else return false;
        if (scope.Aliases.TryGetValue(member, out _))
        {
            var alias = declarationNamespaces.Keys.OfType<AliasDeclaration>().FirstOrDefault(a =>
                a.Name == member && ReferenceEquals(ResolveNamespaceByName("global" +
                    (DeclarationNamespace(a) is { Length: > 0 } aliasNamespace ? "::" + aliasNamespace : "")), scope));
            string ns = alias == null ? "" : DeclarationNamespace(alias) ?? "";
            reflection = new("alias:" + ns + ":" + member, "Alias",
                ns.Length == 0 ? member : ns + "::" + member, ResolveAlias(type), alias);
            return true;
        }
        if (scope.TypeNames.ContainsKey(member))
        {
            reflection = ReflectType(type, type.Line);
            return true;
        }
        var methods = NamespaceMethods(scope, member);
        if (methods.Length != 0)
        {
            if (arguments.Count != 0)
                throw new TypeCheckException("Reflect the method group and select its generic arguments when calling or capturing it", type.Span);
            reflection = ReflectMethodSet(methods, null, member);
            return true;
        }
        return false;
    }

    private AstNode? ReflectionNamespaceQualifier(TypeExpression syntax)
    {
            if (syntax is NamedTypeExpression { TypeArguments.Count: 0 } named)
            {
                if (named.IsResolvedNominal || IsPrimitive(named.Name) || IsReflectionType(named)) return null;
                if (named.Namespace == "global")
                {
                    if (_globalScope.Aliases.ContainsKey(named.Name) || _globalScope.TypeNames.ContainsKey(named.Name)) return null;
                    return new NamespaceAccessExpression(new GlobalExpression(named.Line), named.Name, named.Line);
                }
                if (named.Namespace != null) return null;
                if (TryLookupVariable(named.Name, out _) || _localAliases.Any(a => a.ContainsKey(named.Name))) return null;
                for (var lexical = _currentNamespace; lexical != null; lexical = lexical.Parent)
                    if (lexical.Aliases.ContainsKey(named.Name) || lexical.TypeNames.ContainsKey(named.Name)) return null;
                return new IdentifierExpression(named.Name, named.Line);
            }
            if (syntax is NestedTypeExpression { TypeArguments.Count: 0 } parent && ReflectionNamespaceQualifier(parent.Parent) is { } outer &&
                ResolveNamespace(outer) is { } parentScope && !parentScope.Aliases.ContainsKey(parent.Member) &&
                !parentScope.TypeNames.ContainsKey(parent.Member))
                return new NamespaceAccessExpression(outer, parent.Member, parent.Line);
            return null;
    }

    private string ReflectedMethodIdentity(MethodDeclaration method, TypeExpression? owner)
        => "method:" + (owner == null ? GetFunctionNamespace(method) : GetTypeMangledName(owner)) + ":" +
            method.Span.SourceId?.Path + ":" + method.Span.Start + ":" + method.Name;

    private ConstValue.Reflection ReflectMethodSet(IReadOnlyList<MethodDeclaration> methods, TypeExpression? owner, string name)
    {
        if (methods.Count == 0) throw new InvalidOperationException("A reflected method set requires a declaration");
        string identity = methods.Count == 1 ? ReflectedMethodIdentity(methods[0], owner)
            : "overloads:" + string.Join("|", methods.Select(method => ReflectedMethodIdentity(method, owner)).OrderBy(id => id, StringComparer.Ordinal));
        return new(identity, methods.Count == 1 ? "Method" : "OverloadGroup", name, null,
            methods.Count == 1 ? methods[0] : null, owner, Overloads: methods.ToArray());
    }

    private ConstValue.Reflection ReflectMethodType(MethodDeclaration method, TypeExpression type, int line, TypeExpression? owner = null)
    {
        var formal = GenericFormalType(method, type);
        if (HasUnboundExtensionType(formal, method.GenericParameters.Select(p => p.Name).ToHashSet()))
            return new("formal-type:" + ReflectedMethodIdentity(method, owner) + ":" + TypeName(formal),
                "Type", TypeName(formal), formal);
        return ReflectType(formal, line);
    }

    private bool TryReflectMethodMember(ConstValue.Reflection target, string member, int line, out ConstValue? value)
    {
        value = null;
        if (target.Declaration is Parameter parameterDeclaration)
        {
            if (member == "Type" && target.Type != null)
                value = reflectedParameterTypes.GetValueOrDefault(target.Identity) ?? ReflectType(target.Type, line);
            else if (member == "Position") value = target.Value;
            else if (member == "IsConst") value = new ConstValue.Boolean(parameterDeclaration.IsConst);
            else return false;
            return true;
        }
        if (member == "Overloads" && target.Overloads != null)
        {
            value = new ConstValue.Array(target.Overloads.Select(method =>
                (ConstValue)ReflectMethodSet([method], target.DeclaringType, method.Name)).ToList());
            return true;
        }
        if (target.Declaration is not MethodDeclaration declaration) return false;
        if (member is "Parameters" or "ParameterTypes" or "ParameterCount" or "ReturnType")
        {
            BindMethodSignature(declaration);
            using var signature = EnterMethodSignatureContext(declaration);
            RegisterParameterDefaults(declaration, declaration.Parameters);
        }
        if (member == "Parameters")
            value = new ConstValue.Array(declaration.Parameters.Select((parameter, index) => {
                string identity = ReflectedDeclarationIdentity(parameter) + ":" + parameter.Name;
                var type = ReflectMethodType(declaration, parameter.Type, line, target.DeclaringType);
                reflectedParameterTypes[identity] = type;
                return (ConstValue)new ConstValue.Reflection(identity, "Parameter", parameter.Name, type.Type,
                    parameter, target.DeclaringType, new ConstValue.UInteger((ulong)index));
            }).ToList());
        else if (member == "ParameterTypes")
            value = new ConstValue.Array(declaration.Parameters.Select(parameter => (ConstValue)ReflectMethodType(declaration, parameter.Type, line, target.DeclaringType)).ToList());
        else if (member == "ReturnType") value = ReflectMethodType(declaration, declaration.ReturnType, line, target.DeclaringType);
        else return false;
        return true;
    }

    private MethodDeclaration[] ReflectedCallDefinitions(CallExpression call, IEnumerable<MethodDeclaration> fallback)
        => reflectedCallCandidates.TryGetValue(call, out var captured) ? captured : fallback.ToArray();

    private bool TrySelectReflectedOverload(MethodDeclaration representative, CallExpression call, out MethodDeclaration selected)
    {
        selected = null!;
        if (!reflectedCallCandidates.TryGetValue(call, out var captured)) return false;
        foreach (var definition in captured) BindMethodSignature(definition);
        var candidate = SelectCallableOverload(captured, call, representative.Name);
        selected = candidate.Definition;
        if (selected.IsGeneric) selectedGenericCallArguments[call] = candidate.TypeArguments;
        else selectedGenericCallArguments.Remove(call);
        return true;
    }

    private AstNode ReflectedNamespaceCallee(MethodDeclaration selected, string name, int line)
    {
        AstNode owner = new GlobalExpression(line);
        string ns = DeclarationNamespace(selected) ?? GetFunctionNamespace(selected).Replace("$", "::");
        foreach (string part in ns.Split("::", StringSplitOptions.RemoveEmptyEntries))
            owner = new NamespaceAccessExpression(owner, part, line);
        return new NamespaceAccessExpression(owner, name, line);
    }

    private AstNode ReflectedReceiver(SpliceExpression splice, ConstValue.Reflection reflection)
    {
        var receiver = splice.Receiver!;
        receiver.Accept(this);
        if (reflection.DeclaringType is not { } owner)
            throw new TypeCheckException("A namespace function does not take an instance receiver", splice.Span);
        var actual = ResolveAlias(GetType(receiver));
        var element = actual switch { PointerTypeExpression pointer => pointer.Inner,
            ManagedTypeExpression managed => managed.Inner, _ => actual };
        element = ResolveAlias(element);
        owner = ResolveAlias(owner);
        bool same = TypesMatch(element, owner);
        if (!same && (owner is not NamedTypeExpression named || !TypeDerivesFromClass(element, named.Name)))
            throw new TypeCheckException($"Reflected method requires a receiver of '{DisplayTypeName(owner)}'", splice.Span);
        // Keep lookup anchored to the reflected declaration's owner. A derived
        // class may declare unrelated overloads or hide a nonvirtual method.
        if (!same && actual is PointerTypeExpression or ManagedTypeExpression)
        {
            owner = owner.WithQualifiersFrom(element);
            TypeExpression view = actual switch {
                PointerTypeExpression pointer => new PointerTypeExpression(owner, pointer.IsNullable, splice.Line, pointer.IsReadOnly),
                ManagedTypeExpression managed => new ManagedTypeExpression(owner, managed.IsNullable, splice.Line, managed.IsReadOnly, managed.Collector),
                _ => throw new InvalidOperationException()
            };
            var cast = new CastExpression(view.WithQualifiersFrom(actual), receiver, splice.Line) { Span = receiver.Span };
            RecordType(cast, view.WithQualifiersFrom(actual));
            receiver = cast;
        }
        return receiver;
    }

    private bool TryCheckReflectedMethodCall(CallExpression node)
    {
        if (node.Callee is not SpliceExpression splice) return false;
        var reflection = RequireReflection(splice.Reflection);
        if (reflection.Kind is not ("Method" or "OverloadGroup")) return false;
        var methods = reflection.Overloads?.ToArray() ?? (reflection.Declaration is MethodDeclaration method ? [method] : []);
        if (methods.Length == 0) throw new TypeCheckException("Reflected method has no declaration", splice.Span);
        bool instance = reflection.DeclaringType != null && methods.Any(method => !method.IsStatic);
        if (instance && splice.Receiver == null)
            throw new TypeCheckException("An instance method splice requires a receiver", splice.Span);
        if (!instance && splice.Receiver != null)
            throw new TypeCheckException("A static or namespace method splice does not take a receiver", splice.Span);
        var receiver = splice.Receiver == null ? null : ReflectedReceiver(splice, reflection);
        foreach (var argument in node.Arguments) CheckNullableArgument(argument);
        foreach (var definition in methods) BindMethodSignature(definition);
        var choice = SelectCallableOverload(methods, node, reflection.Name);
        ValidateReflectedMethodAccess(choice.Definition, node.Callee.Span);
        AstNode callee = receiver != null ? new MemberAccessExpression(receiver, reflection.Name, false, node.Line)
            : reflection.DeclaringType != null ? new NamespaceAccessExpression(reflection.DeclaringType, reflection.Name, node.Line)
            : ReflectedNamespaceCallee(choice.Definition, reflection.Name, node.Line);
        callee.Span = splice.Span;
        var expanded = new CallExpression(callee, node.Arguments, node.Line, node.TypeArguments) { Span = node.Span };
        reflectedCallCandidates[expanded] = [choice.Definition];
        checkedExtensionArguments.Add(expanded);
        if (receiver != null) checkedPrimitiveReceivers.Add(receiver);
        try { expanded.Accept(this); }
        finally
        {
            reflectedCallCandidates.Remove(expanded);
            if (receiver != null) checkedPrimitiveReceivers.Remove(receiver);
        }
        reflectedCalls[node] = expanded;
        if (!choice.Definition.IsVirtual && !choice.Definition.IsOverride && !choice.Definition.IsAbstract)
            _virtualMethodCalls.Remove(expanded);
        splice.Resolved = callee;
        RecordType(splice, GetType(callee));
        RecordType(node, GetType(expanded));
        if (_resolvedCalls.TryGetValue(expanded, out var target)) _resolvedCalls[node] = target;
        if (_virtualMethodCalls.TryGetValue(expanded, out var virtualCall)) _virtualMethodCalls[node] = virtualCall;
        if (_interfaceMethodCalls.TryGetValue(expanded, out var interfaceCall)) _interfaceMethodCalls[node] = interfaceCall;
        if (selectedGenericCallArguments.TryGetValue(expanded, out var arguments)) selectedGenericCallArguments[node] = arguments;
        if (TryGetConstValue(expanded, out var constant)) _constValues[node] = constant!;
        RecordExceptionCall(node);
        return true;
    }

    private void ValidateReflectedMethodAccess(MethodDeclaration method, SourceSpan use)
    {
        ValidateNamespaceFunctionAccess(method, use);
        if (!TryGetMethodOwner(method, out var owner, out bool isClass)) return;
        if (isClass)
        {
            if (!CanAccessClassMethod(method, owner))
                throw new TypeCheckException($"Cannot access {method.Accessibility.ToString().ToLowerInvariant()} method '{method.Name}' of class '{DisplayName(owner)}'", use);
        }
        else if (GetStructByIdentity(owner) is { } structure)
            ValidateStructMemberAccess(structure, method.Name, method.Accessibility, true, use);
    }

    private bool TryBindReflectedFunctionValue(AstNode expression, SpliceExpression splice, ConstValue.Reflection reflection)
    {
        if (reflection.Kind is not ("Method" or "OverloadGroup")) return false;
        if (splice.Receiver != null)
            throw new TypeCheckException("An instance method splice requires an immediate call", splice.Span);
        var methods = reflection.Overloads?.ToArray() ?? (reflection.Declaration is MethodDeclaration method ? [method] : []);
        if (reflection.DeclaringType != null && methods.Any(method => !method.IsStatic))
            throw new TypeCheckException("Instance method splices require a receiver and an immediate call", splice.Span);
        if (methods.Length == 0) throw new TypeCheckException("Reflected method has no declaration", splice.Span);
        reflectedFunctionValues[expression] = reflection;
        importedFunctionValueCandidates[expression] = methods;
        _functionAddressTargets[expression] = methods[0];
        bool needsTarget = methods.Length != 1 || methods[0].IsGeneric;
        if (!needsTarget)
        {
            ValidateReflectedFunctionSelection(expression, methods[0]);
            ValidateFunctionAddress(methods[0], expression.Line);
            BindMethodSignature(methods[0]);
            selectedFunctionValues.Add(expression);
        }
        RecordType(expression, needsTarget ? new FunctionPointerTypeExpression(Void, [], false, false, expression.Line)
            : FunctionValueType(methods[0], expression.Line, expression));
        return true;
    }

    private void ValidateReflectedFunctionSelection(AstNode expression, MethodDeclaration selected)
    {
        if (!reflectedFunctionValues.TryGetValue(expression, out var reflection)) return;
        ValidateReflectedMethodAccess(selected, expression.Span);
        if (reflection.DeclaringType != null)
            ValidateAliasMember(new NamespaceAccessExpression(reflection.DeclaringType, reflection.Name, expression.Line)
                { Span = expression.Span }, selected);
    }
}
