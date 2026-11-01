using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<(AstNode Definition, string View), MethodDeclaration> reflectionMethodProofs = new();
    private readonly Dictionary<MethodDeclaration, IReadOnlyDictionary<string, ConstValue>> reflectionStageInputs = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MethodDeclaration> deferredReflectionBodies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(MethodDeclaration Method, string View), MethodDeclaration> reflectionOwnerProofs = new();
    private readonly Dictionary<CallExpression, MethodDeclaration> reflectionOwnerCallTargets = new();
    private readonly Dictionary<AstNode, NamedTypeExpression> reflectionOwnerViews = new(ReferenceEqualityComparer.Instance);
    private int reflectionStageDepth;
    public bool IsDeferredReflectionBody(MethodDeclaration method) => deferredReflectionBodies.Contains(method);

    private TypeExpression RefineReflectionThisType(string name, TypeExpression type)
    {
        if (name != "this" || CurrentReflectionOwnerView() is not { } owner) return type;
        return type switch {
            PointerTypeExpression pointer => new PointerTypeExpression(owner.WithQualifiersFrom(pointer.Inner), pointer.IsNullable, pointer.Line, pointer.IsReadOnly)
                .WithQualifiersFrom(pointer),
            ManagedTypeExpression managed => new ManagedTypeExpression(owner.WithQualifiersFrom(managed.Inner), managed.IsNullable, managed.Line, managed.IsReadOnly, managed.Collector)
                .WithQualifiersFrom(managed), _ => type
        };
    }

    private NamedTypeExpression? CurrentReflectionOwnerView()
        => _currentFunction != null && reflectionOwnerViews.TryGetValue(_currentFunction, out var owner) ? owner
            : currentSpecializationBody != null && reflectionOwnerViews.TryGetValue(currentSpecializationBody, out owner) ? owner : null;

    private NamedTypeExpression? ReflectionDeclaringOwnerView(TypeExpression receiver, string identity)
    {
        while (receiver is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
            receiver = receiver switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner,
                ReferenceTypeExpression reference => reference.Inner, _ => receiver };
        if (receiver is not NamedTypeExpression start) return null;
        return Find(start, new());
        NamedTypeExpression? Find(NamedTypeExpression view, HashSet<string> active)
        {
            if (view.Name == identity) return view;
            if (!active.Add(view.Name)) return null;
            try
            {
                var origin = view.GenericUse ?? GetGenericTypeOrigin(view.Name);
                var declaration = origin == null ? GetClassByIdentity(view.Name)?.Declaration : _genericClasses.GetValueOrDefault(origin.Name);
                if (declaration == null) return null;
                var bindings = origin == null ? new Dictionary<string, TypeExpression>()
                    : ReflectionProofBindings(declaration, declaration.GenericParameters, origin.TypeArguments);
                foreach (var baseType in declaration.BaseTypes)
                {
                    var cloned = new AstCloner(bindings) { TypeCloned = PreserveReflectionAliasViews }.CloneType(baseType);
                    using var context = EnterSignatureContext(declaration, view.Name, true);
                    if (ResolveAlias(cloned) is NamedTypeExpression parent && GetClassByIdentity(parent.Name) != null && Find(parent, active) is { } found)
                        return found;
                }
                return null;
            }
            finally { active.Remove(view.Name); }
        }
    }

    private TypeExpression ReflectionOwnerFieldType(TypeExpression? receiver, string name, TypeExpression fallback)
    {
        receiver ??= CurrentReflectionOwnerView();
        if (receiver == null && TryLookupVariable("this", out var self)) receiver = self;
        if (receiver == null) return fallback;
        var element = receiver;
        while (element is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
            element = element switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner,
                ReferenceTypeExpression reference => reference.Inner, _ => element };
        if (element is not NamedTypeExpression named) return fallback;
        if (named.GenericUse == null && CurrentReflectionOwnerView() is { } current &&
            ReflectionDeclaringOwnerView(current, named.Name) is { } currentView)
            receiver = named = (NamedTypeExpression)currentView.WithQualifiersFrom(named);
        FieldDeclaration? field;
        string owner;
        if (GetClassByIdentity(named.Name) is { } cls)
        {
            field = cls.FieldDeclarationsByName.GetValueOrDefault(name);
            owner = cls.Fields.FirstOrDefault(f => f.Name == name).DeclaringClass ?? named.Name;
            if (field != null && (field.IsStatic || field.IsConst) && staticStorage.TryGetValue(field, out var storage))
                owner = storage.Owner ?? owner;
        }
        else if (GetStructByIdentity(named.Name) is { } structure)
        {
            field = structure.FieldDeclarationsByName.GetValueOrDefault(name);
            owner = structure.Name;
        }
        else return fallback;
        if (field == null || ReflectionDeclaringOwnerView(receiver, owner) is not { } ownerView) return fallback;
        var origin = ownerView.GenericUse ?? GetGenericTypeOrigin(ownerView.Name);
        if (origin == null || !origin.TypeArguments.Any(ContainsAliasContract)) return fallback;
        AstNode? definition = _genericClasses.GetValueOrDefault(origin.Name) ?? (AstNode?)_genericStructs.GetValueOrDefault(origin.Name);
        if (definition == null) return fallback;
        var parameters = definition is ClassDeclaration sourceClass ? sourceClass.GenericParameters : ((StructDeclaration)definition).GenericParameters;
        var members = definition is ClassDeclaration declaration ? declaration.Members : ((StructDeclaration)definition).Members;
        var original = members.OfType<FieldDeclaration>().FirstOrDefault(f => f.Name == field.Name && f.Span == field.Span);
        if (original == null) return fallback;
        var clone = new AstCloner(ReflectionProofBindings(definition, parameters, origin.TypeArguments)) {
            TypeCloned = PreserveReflectionAliasViews
        }.CloneType(original.Type);
        using var context = EnterFieldLayoutContext(owner, name);
        var rebound = ResolveAlias(clone);
        var result = rebound.WithQualifiersFrom(fallback);
        PreserveReflectionAliasViews(rebound, result);
        return result;
    }

    private static IEnumerable<(string Path, AstNode Node)> ReflectionChildren(AstNode node)
    {
        if (node is StaticIfStatement conditional) yield return ("Condition", conditional.Condition);
        if (node is StaticForeachStatement loop) yield return ("Collection", loop.Collection);
        if (node is BlockStatement block)
        {
            for (int i = 0; i < block.Statements.Count; i++) yield return ("Statements/" + i, block.Statements[i]);
            yield break;
        }
        foreach (var property in node.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.Name != "Resolved"))
        {
            if (property.GetValue(node) is AstNode child) yield return (property.Name, child);
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                int index = 0;
                foreach (var item in children) yield return (property.Name + "/" + index++, item);
            }
        }
    }

    private static IEnumerable<AstNode> ReflectionSourceNodes(AstNode node)
    {
        yield return node;
        // A source static expansion has not populated Statements yet.
        if (node is StaticIfStatement conditional)
        {
            foreach (var item in ReflectionSourceNodes(conditional.Condition)) yield return item;
            foreach (var item in ReflectionSourceNodes(conditional.ThenBody)) yield return item;
            if (conditional.ElseBody != null)
                foreach (var item in ReflectionSourceNodes(conditional.ElseBody)) yield return item;
            yield break;
        }
        if (node is StaticForeachStatement loop)
        {
            foreach (var item in ReflectionSourceNodes(loop.Collection)) yield return item;
            foreach (var item in ReflectionSourceNodes(loop.Body)) yield return item;
            yield break;
        }
        foreach (var child in ReflectionChildren(node))
            foreach (var item in ReflectionSourceNodes(child.Node)) yield return item;
    }

    public bool RequiresReflectionStaging(MethodDeclaration method)
    {
        if (!method.IsConst || method.Body == null || method.Parameters.Count == 0) return false;
        var parameters = method.Parameters.Select(p => p.Name).ToHashSet();
        var locals = ReflectionSourceNodes(method.Body).OfType<VariableDeclaration>().Where(v => v.Initializer != null)
            .GroupBy(v => v.Name).ToDictionary(g => g.Key, g => g.Select(v => v.Initializer!).ToArray());
        return ReflectionSourceNodes(method.Body).Any(node => node switch {
            StaticIfStatement conditional => Depends(conditional.Condition, new()),
            StaticForeachStatement loop => Depends(loop.Collection, new()),
            SpliceExpression splice => Depends(splice.Reflection, new()),
            SpliceTypeExpression splice => Depends(splice.Reflection, new()), _ => false
        });

        bool Depends(AstNode node, HashSet<string> active)
        {
            if (node is TypeofExpression or NameofExpression) return false;
            if (node is ReflectExpression { Target: TypeExpression or IdentifierExpression }) return false;
            if (node is IdentifierExpression identifier)
            {
                if (parameters.Contains(identifier.Name)) return true;
                if (!active.Add(identifier.Name)) return false;
                try { return locals.TryGetValue(identifier.Name, out var values) && values.Any(v => Depends(v, active)); }
                finally { active.Remove(identifier.Name); }
            }
            return ReflectionChildren(node).Any(child => Depends(child.Node, active));
        }
    }

    private bool DeferReflectionConstBody(MethodDeclaration method)
    {
        if (!RequiresReflectionStaging(method) || reflectionStageInputs.ContainsKey(method)) return false;
        deferredReflectionBodies.Add(method);
        return true;
    }

    private void BindReflectionStageParameter(Parameter parameter)
    {
        if (_currentFunction is not MethodDeclaration method || !reflectionStageInputs.TryGetValue(method, out var inputs) ||
            !inputs.TryGetValue(parameter.Name, out var value)) return;
        var type = ParameterValueType(parameter.Type);
        if (type is ArrayTypeExpression array && value is ConstValue.Array elements)
            _scopes.Peek()[parameter.Name] = type = new ArrayTypeExpression(array.ElementType, elements.Elements.Count, array.Line)
                .WithQualifiersFrom(array);
        var declaration = new VariableDeclaration(parameter.Name, type, null, parameter.Line, isConst: true) { Span = parameter.Span };
        localConstants.Peek()[parameter.Name] = (value, declaration);
        _constValues[declaration] = value;
    }

    private void PreserveReflectionAliasViews(TypeExpression source, TypeExpression result)
    {
        if (aliasContracts.TryGetValue(source, out var contract)) aliasContracts[result] = contract;
        switch (source, result)
        {
            case (PointerTypeExpression first, PointerTypeExpression second): PreserveReflectionAliasViews(first.Inner, second.Inner); break;
            case (ManagedTypeExpression first, ManagedTypeExpression second):
                PreserveReflectionAliasViews(first.Inner, second.Inner);
                if (first.Collector != null && second.Collector != null) PreserveReflectionAliasViews(first.Collector, second.Collector);
                break;
            case (ReferenceTypeExpression first, ReferenceTypeExpression second): PreserveReflectionAliasViews(first.Inner, second.Inner); break;
            case (ArrayTypeExpression first, ArrayTypeExpression second): PreserveReflectionAliasViews(first.ElementType, second.ElementType); break;
            case (FunctionPointerTypeExpression first, FunctionPointerTypeExpression second):
                PreserveReflectionAliasViews(first.ReturnType, second.ReturnType);
                foreach (var pair in first.ParameterTypes.Zip(second.ParameterTypes)) PreserveReflectionAliasViews(pair.First, pair.Second);
                break;
            case (NamedTypeExpression first, NamedTypeExpression second):
                foreach (var pair in (first.GenericUse?.TypeArguments ?? first.TypeArguments).Zip(second.GenericUse?.TypeArguments ?? second.TypeArguments))
                    PreserveReflectionAliasViews(pair.First, pair.Second);
                break;
        }
    }

    private Dictionary<string, TypeExpression> ReflectionProofBindings(AstNode definition, List<GenericParameter> parameters, List<TypeExpression> arguments)
    {
        var bindings = ConstrainedTypeArguments(definition, parameters, arguments);
        foreach (var pair in parameters.Zip(arguments)) PreserveReflectionAliasViews(pair.Second, bindings[pair.First.Name]);
        return bindings;
    }

    private MethodDeclaration CloneReflectionMethod(MethodDeclaration method, Dictionary<string, TypeExpression> bindings, bool closeParameters)
        => new AstCloner(bindings, closeParameters ? method.Name : null, renameTypeOwner: false) {
            TypeCloned = PreserveReflectionAliasViews
        }.CloneMethod(method);

    private void CheckPrivateReflectionMethod(MethodDeclaration source, MethodDeclaration clone, string? owner = null, bool ownerIsClass = false)
    {
        if (owner != null) specializedMethodOwners[clone] = (owner, ownerIsClass);
        else if (TryGetMethodOwner(source, out var declaredOwner, out bool declaredClass)) specializedMethodOwners[clone] = (declaredOwner, declaredClass);
        CheckSpecialization(source, clone);
        if (!IsDeferredReflectionBody(clone))
        {
            var flow = new semantics.ControlFlowPass(this, _constEvaluator, _diagnostics);
            flow.CheckMethod(clone);
            new semantics.DefiniteAssignmentPass(this, flow).CheckMethod(clone);
        }
    }

    private void CheckPrivateOwnerMember(AstNode source, AstNode clone, NamedTypeExpression owner, bool isClass)
    {
        using var context = SourceContext.Enter(source.Span);
        using var locals = IsolateDeclarationContext();
        using var nullable = IsolateNullableFlow();
        using var exceptions = IsolateExceptionHandlers();
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        var previousFunction = _currentFunction;
        var previousDefinition = currentSpecializationDefinition;
        var previousBody = currentSpecializationBody;
        _currentClass = isClass ? GetClassByIdentity(owner.Name) : null;
        _currentStruct = isClass ? null : GetStructByIdentity(owner.Name);
        _currentNamespacePath = DeclarationNamespace(source) ?? (_currentClass?.Namespace ?? _currentStruct?.Namespace ?? "").Replace("$", "::");
        _currentNamespace = _currentNamespacePath.Length == 0 ? _globalScope :
            ResolveNamespaceByName("global::" + _currentNamespacePath)
            ?? throw new InvalidOperationException("A checked member has no declaring namespace");
        currentSpecializationDefinition = source;
        currentSpecializationBody = clone;
        reflectionOwnerViews[clone] = owner;
        declarationNamespaces[clone] = _currentNamespacePath;
        if (clone is MethodDeclaration method) specializedMethodOwners[method] = (owner.Name, isClass);
        if (clone is FieldDeclaration field)
            _currentFunction = new MethodDeclaration("$view_initializer", Void, [], null, TokenKind.Private,
                field.IsStatic || field.IsConst, false, false, false, field.Line);
        try
        {
            if (clone is ConstructorDeclaration constructor)
                BindConstructorSignature(constructor, owner.Name, isClass);
            else if (clone is OperatorDeclaration operation && _currentStruct != null)
                BindOperatorSignature(operation, _currentStruct);
            clone.Accept(this);
        }
        finally
        {
            _currentNamespace = previousNamespace;
            _currentNamespacePath = previousPath;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
            _currentFunction = previousFunction;
            currentSpecializationDefinition = previousDefinition;
            currentSpecializationBody = previousBody;
        }
    }

    private bool TryValidateReflectionAliasProof(AstNode definition, List<GenericParameter> parameters,
        List<TypeExpression> arguments, AstNode? canonical)
    {
        if (canonical == null) return false;
        var bindings = ReflectionProofBindings(definition, parameters, arguments);
        if (definition is MethodDeclaration method && canonical is MethodDeclaration body)
        {
            string view = string.Join(";", arguments.Select(AliasContractArgumentKey));
            var key = ((AstNode)method, view);
            if (reflectionMethodProofs.ContainsKey(key)) return true;
            var clone = CloneReflectionMethod(method, bindings, closeParameters: true);
            reflectionMethodProofs[key] = clone;
            try
            {
                CheckPrivateReflectionMethod(method, clone);
                CheckReflectionProofCalls(body, clone, definition, parameters, arguments);
            }
            catch { reflectionMethodProofs.Remove(key); throw; }
            return true;
        }
        if (definition is not (ClassDeclaration or StructDeclaration)) return false;
        string owner = canonical is ClassDeclaration ownerClass ? ownerClass.Name : ((StructDeclaration)canonical).Name;
        string originalName = definition is ClassDeclaration originalClass ? originalClass.Name : ((StructDeclaration)definition).Name;
        var privateType = new AstCloner(bindings, originalName, owner) { TypeCloned = PreserveReflectionAliasViews }.CloneNode(definition);
        var ownerView = (NamedTypeExpression)NamedTypeExpression.Resolved(owner, privateType.Line).WithGenericUse(
            new NamedTypeExpression(originalName, null, privateType.Line, arguments));
        CheckMembers(canonical, privateType, ownerView);
        var privateUnit = new CompilationUnit([], [], [privateType], privateType.Line);
        var privateFlow = new semantics.ControlFlowPass(this, _constEvaluator, _diagnostics);
        privateFlow.Execute(privateUnit);
        new semantics.DefiniteAssignmentPass(this, privateFlow).Execute(privateUnit);
        // Existing structural checking still covers fields, bases, and ordinary members.
        return false;

        void CheckMembers(AstNode checkedType, AstNode privateDeclaration, NamedTypeExpression view)
        {
            var checkedMembers = checkedType is ClassDeclaration checkedClass ? checkedClass.Members : ((StructDeclaration)checkedType).Members;
            var privateMembers = privateDeclaration is ClassDeclaration privateClass ? privateClass.Members : ((StructDeclaration)privateDeclaration).Members;
            foreach (var clone in privateMembers)
            {
                bool nested = clone is ClassDeclaration { IsGeneric: false } or StructDeclaration { IsGeneric: false };
                if (!nested && clone is not (FieldDeclaration or ConstructorDeclaration or DestructorDeclaration or OperatorDeclaration) &&
                    clone is not MethodDeclaration { IsGeneric: false, Body: not null }) continue;
                var checkedMember = checkedMembers.First(m => m.GetType() == clone.GetType() && m.Span == clone.Span &&
                    (m is not MethodDeclaration candidateMethod || clone is MethodDeclaration counterpart && candidateMethod.Name == counterpart.Name));
                if (nested)
                {
                    string nestedName = checkedMember is ClassDeclaration nestedClass ? nestedClass.Name : ((StructDeclaration)checkedMember).Name;
                    CheckMembers(checkedMember, clone, NamedTypeExpression.Resolved(nestedName, clone.Line));
                }
                else
                {
                    CheckPrivateOwnerMember(checkedMember, clone, view, checkedType is ClassDeclaration);
                    CheckReflectionProofCalls(checkedMember, clone, definition, parameters, arguments);
                }
            }
        }
    }

    private static Dictionary<string, CallExpression> ReflectionProofCalls(AstNode declaration)
    {
        var calls = new Dictionary<string, CallExpression>();
        if (declaration is MethodDeclaration { Body: not null } method) Visit(method.Body, "Body");
        else Visit(declaration, "Member");
        return calls;
        void Visit(AstNode node, string path)
        {
            if (node is CallExpression call) calls[path] = call;
            foreach (var child in ReflectionChildren(node)) Visit(child.Node, path + "/" + child.Path);
        }
    }

    private CallExpression ReflectionEffectiveCall(CallExpression call)
    {
        while ((GetReflectedCall(call) ?? GetExtensionCall(call)) is { } expanded) call = expanded;
        return call;
    }

    private TypeExpression RebindReflectionProofType(TypeExpression type, AstNode definition,
        List<GenericParameter> parameters, List<TypeExpression> arguments)
    {
        if (type.GenericParameterView is { } view && ReferenceEquals(view.DeclaringDefinition, definition))
        {
            int index = parameters.FindIndex(p => p.Name == view.ParameterName);
            if (index >= 0)
            {
                var replacement = arguments[index].WithQualifiersFrom(type);
                PreserveReflectionAliasViews(arguments[index], replacement);
                return replacement;
            }
        }
        TypeExpression Child(TypeExpression child) => RebindReflectionProofType(child, definition, parameters, arguments);
        var result = type switch {
            PointerTypeExpression pointer => new PointerTypeExpression(Child(pointer.Inner), pointer.IsNullable, pointer.Line, pointer.IsReadOnly),
            ManagedTypeExpression managed => new ManagedTypeExpression(Child(managed.Inner), managed.IsNullable, managed.Line, managed.IsReadOnly,
                managed.Collector == null ? null : Child(managed.Collector)),
            ReferenceTypeExpression reference => new ReferenceTypeExpression(Child(reference.Inner), reference.Line),
            ArrayTypeExpression array => new ArrayTypeExpression(Child(array.ElementType), array.Size, array.Line, array.SizeExpression),
            FunctionPointerTypeExpression function => new FunctionPointerTypeExpression(Child(function.ReturnType), function.ParameterTypes.Select(Child).ToList(),
                function.IsManaged, function.IsNullable, function.Line, function.IsSignature),
            NamedTypeExpression named when named.GenericUse is { } use => new NamedTypeExpression(use.Name, use.Namespace, named.Line, use.TypeArguments.Select(Child).ToList()),
            NamedTypeExpression named when named.TypeArguments.Count != 0 => new NamedTypeExpression(named.Name, named.Namespace, named.Line, named.TypeArguments.Select(Child).ToList()),
            _ => type
        };
        result = result.WithQualifiersFrom(type);
        PreserveReflectionAliasViews(type, result);
        return result;
    }

    private void CheckReflectionProofCalls(AstNode canonical, AstNode clone, AstNode definition,
        List<GenericParameter> parameters, List<TypeExpression> arguments)
    {
        var checkedCalls = ReflectionProofCalls(clone);
        foreach (var occurrence in ReflectionProofCalls(canonical))
        {
            if (!checkedCalls.TryGetValue(occurrence.Key, out var rebound))
                throw new TypeCheckException("A constrained alias view changes this compile-time expansion", occurrence.Value.Span);
            var emitted = ReflectionEffectiveCall(occurrence.Value);
            var checkedCall = ReflectionEffectiveCall(rebound);
            if (GetResolvedCall(emitted) is not MethodDeclaration selected) continue;
            if (checkedCall.Callee is NamespaceAccessExpression access) ValidateAliasMember(access, GetGenericMethodDefinition(selected) ?? selected);
            if (GetGenericMethodDefinition(selected) is not MethodDeclaration target || GetGenericCallTypeArguments(emitted) is not { } typeArguments) continue;
            var reboundArguments = typeArguments.Select(t => RebindReflectionProofType(t, definition, parameters, arguments)).ToList();
            ValidateAliasSpecialization(target, target.GenericParameters, reboundArguments);
        }
    }

    public MethodDeclaration ReflectionComptimeCallTarget(CallExpression call, MethodDeclaration method)
    {
        if (reflectionOwnerCallTargets.TryGetValue(ReflectionEffectiveCall(call), out var ownerProof)) return ownerProof;
        if (GetGenericMethodDefinition(method) is not MethodDeclaration definition ||
            GetGenericCallTypeArguments(call) is not { } arguments || !arguments.Any(ContainsAliasContract)) return method;
        string view = string.Join(";", arguments.Select(AliasContractArgumentKey));
        if (reflectionMethodProofs.TryGetValue((definition, view), out var proof)) return proof;
        ValidateAliasSpecialization(definition, definition.GenericParameters, arguments.ToList());
        return reflectionMethodProofs.TryGetValue((definition, view), out proof) ? proof
            : throw new TypeCheckException("A constrained reflection call has no checked implementation view", call.Span);
    }

    public MethodDeclaration PrepareReflectionConstInvocation(CallExpression call, MethodDeclaration method, IReadOnlyList<ConstValue> arguments)
    {
        method = ReflectionComptimeCallTarget(call, method);
        if (!RequiresReflectionStaging(method)) return method;
        if (++reflectionStageDepth > 256)
        {
            reflectionStageDepth--;
            throw new TypeCheckException("Compile-time reflection staging exceeded maximum call depth", call.Span);
        }
        try
        {
            var clone = CloneReflectionMethod(method, new(), closeParameters: false);
            if (reflectionOwnerViews.TryGetValue(method, out var ownerView)) reflectionOwnerViews[clone] = ownerView;
            reflectionStageInputs[clone] = method.Parameters.Select((parameter, index) => (parameter, index))
                .Where(pair => pair.parameter.IsConst).ToDictionary(pair => pair.parameter.Name, pair => arguments[pair.index]);
            CheckPrivateReflectionMethod(method, clone);
            return clone;
        }
        finally { reflectionStageDepth--; }
    }

    private void ValidateReflectionOwnerMethodCall(CallExpression call, MethodDeclaration selected, IReadOnlyList<TypeExpression> methodArguments)
    {
        TypeExpression? receiver = call.Callee switch {
            MemberAccessExpression member => GetType(member.Object),
            NamespaceAccessExpression access when StaticOwnerType(access.Left) is { } type => ResolveAlias(type),
            IdentifierExpression when TryLookupVariable("this", out var self) => self,
            IdentifierExpression when CurrentReflectionOwnerView() is { } currentOwner => currentOwner,
            _ => null
        };
        while (receiver is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
            receiver = receiver switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner,
                ReferenceTypeExpression reference => reference.Inner, _ => receiver };
        if (receiver == null || !TryGetMethodOwner(selected, out string declaringOwner, out _)) return;
        if (ReflectionDeclaringOwnerView(receiver, declaringOwner) is not { } owner) return;
        var origin = owner.GenericUse ?? GetGenericTypeOrigin(owner.Name);
        if (origin == null || !origin.TypeArguments.Any(ContainsAliasContract)) return;
        AstNode? definition = _genericClasses.GetValueOrDefault(origin.Name) ?? (AstNode?)_genericStructs.GetValueOrDefault(origin.Name);
        if (definition == null) return;
        var parameters = definition is ClassDeclaration cls ? cls.GenericParameters : ((StructDeclaration)definition).GenericParameters;
        var members = definition is ClassDeclaration sourceClass ? sourceClass.Members : ((StructDeclaration)definition).Members;
        var source = members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Name == selected.Name && m.Span == selected.Span);
        if (source == null) return;
        string view = string.Join(";", origin.TypeArguments.Concat(methodArguments).Select(AliasContractArgumentKey));
        var key = (selected, view);
        if (reflectionOwnerProofs.TryGetValue(key, out var cached)) { reflectionOwnerCallTargets[call] = cached; return; }
        var bindings = ReflectionProofBindings(definition, parameters, origin.TypeArguments);
        var template = CloneReflectionMethod(source, bindings, closeParameters: false);
        specializedMethodOwners[template] = (owner.Name, definition is ClassDeclaration);
        declarationNamespaces[template] = DeclarationNamespace(selected) ?? "";
        MethodDeclaration clone;
        if (source.IsGeneric)
        {
            if (source.GenericParameters.Count != methodArguments.Count)
                throw new TypeCheckException("A reflected owner method needs its selected generic arguments", call.Span);
            clone = CloneReflectionMethod(template,
                ReflectionProofBindings(template, template.GenericParameters, methodArguments.ToList()), closeParameters: true);
        }
        else clone = template;
        reflectionOwnerProofs[key] = clone;
        reflectionOwnerCallTargets[call] = clone;
        reflectionOwnerViews[clone] = owner;
        try
        {
            CheckPrivateReflectionMethod(selected, clone, owner.Name, definition is ClassDeclaration);
            if (aliasSpecializationBodies.GetValueOrDefault((selected, AliasNominalArguments(methodArguments))) is MethodDeclaration canonical)
                CheckReflectionProofCalls(canonical, clone, definition, parameters, origin.TypeArguments);
        }
        catch { reflectionOwnerProofs.Remove(key); reflectionOwnerCallTargets.Remove(call); throw; }
    }
}
