using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record FunctionEntry(MethodDeclaration Method, List<AstNode> Members,
        NamespaceScope Scope, AstNode? Owner);

    private void ResolveFunctionReplacements(CompilationUnit unit)
    {
        var entries = new List<FunctionEntry>();
        Collect(unit.Members, _globalScope, null);
        foreach (NamespaceDeclaration ns in unit.Namespaces) Collect(ns.Members, _namespaceScopes[ns], null);
        var previousNamespace = _currentNamespace;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        try
        {
            foreach (var group in entries.GroupBy(e => (e.Scope, e.Owner, e.Method.Name)))
            {
                using var groupContext = SourceContext.Enter(group.First().Method.Span);
                _currentNamespace = group.Key.Scope;
                _currentClass = group.Key.Owner is ClassDeclaration cls ? GetClassByIdentity(cls.Name) : null;
                _currentStruct = group.Key.Owner is StructDeclaration str ? GetStructByIdentity(str.Name) : null;
                var functions = group.ToList();
                foreach (FunctionEntry entry in functions)
                {
                    MethodDeclaration method = entry.Method;
                    using var methodContext = SourceContext.Enter(method.Span);
                    if (method.IsStatic && (method.IsVirtual || method.IsOverride || method.IsAbstract && entry.Owner is not InterfaceDeclaration))
                        throw new TypeCheckException("Static methods cannot be virtual, override or abstract", method.Line);
                    if (method.ImplementationKind != FunctionImplementationKind.Ordinary &&
                        (method.ImplementationKind != FunctionImplementationKind.Expect && method.Body == null || method.IsAbstract || entry.Owner is InterfaceDeclaration))
                        throw new TypeCheckException("weak and replace require a concrete function body", method.Line);
                    if (method.ImplementationKind != FunctionImplementationKind.Ordinary &&
                        entry.Owner is ClassDeclaration or StructDeclaration && !method.IsStatic)
                        throw new TypeCheckException("expect, weak and replace apply to namespace functions and static methods; use virtual and override for instance methods", method.Span);
                    if (method.ImplementationKind != FunctionImplementationKind.Replace) continue;
                    var defaults = functions.Where(e => e.Method.ImplementationKind is FunctionImplementationKind.Weak or FunctionImplementationKind.Expect &&
                        ParametersMatch(e.Method, method)).ToList();
                    if (defaults.Count != 1)
                        throw new TypeCheckException($"replace '{method.Name}' requires exactly one matching weak definition in the same scope", method.Line);
                    MethodDeclaration original = defaults[0].Method;
                    if (!ContractMatches(original, method))
                        throw new TypeCheckException($"Replacement of '{method.Name}' must match the weak function's return type, constraints, accessibility and modifiers", method.Line);
                }
                // Ordinary overloads are validated by PrepareOverloads in their
                // concrete declaration context. This pass only selects implementations.
                if (functions.All(e => e.Method.ImplementationKind == FunctionImplementationKind.Ordinary)) continue;
                if (group.Key.Owner == null && group.Key.Scope.Externs.ContainsKey(group.Key.Name))
                    throw new TypeCheckException($"Function '{group.Key.Name}' cannot have both an extern declaration and a weak or replace definition", functions[0].Method.Line);
                foreach (var sameSignature in functions.GroupBy(e => ParameterKey(e.Method)))
                {
                    var declarations = sameSignature.ToList();
                    if (declarations.Any(e => e.Method.ImplementationKind == FunctionImplementationKind.Expect) &&
                        !declarations.Any(e => e.Method.ImplementationKind == FunctionImplementationKind.Replace))
                        throw new TypeCheckException($"Expected function '{group.Key.Name}' has no implementation; provide replace", declarations[0].Method.Span);
                    if (declarations.Count == 1) continue;
                    var weak = declarations.Where(e => e.Method.ImplementationKind is FunctionImplementationKind.Weak or FunctionImplementationKind.Expect).ToList();
                    var replace = declarations.Where(e => e.Method.ImplementationKind == FunctionImplementationKind.Replace).ToList();
                    if (weak.Count > 1)
                        DuplicateDeclaration(weak[0].Method, weak[1].Method, $"Multiple weak defaults for '{group.Key.Name}'");
                    if (replace.Count > 1)
                        DuplicateDeclaration(replace[0].Method, replace[1].Method, $"Multiple replacements for '{group.Key.Name}'");
                    if (weak.Count != 1 || replace.Count != 1 || declarations.Count != 2)
                        DuplicateDeclaration(declarations[0].Method, declarations[1].Method, $"Duplicate definition of '{group.Key.Name}'; replacing a weak function requires replace");
                    // Remove the unused body before checking bodies or generating code.
                    weak[0].Members.Remove(weak[0].Method);
                }
            }
        }
        finally
        {
            _currentNamespace = previousNamespace;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
        }

        void Collect(List<AstNode> members, NamespaceScope scope, AstNode? owner)
        {
            foreach (AstNode node in members)
            {
                if (node is MethodDeclaration method)
                {
                    if (owner == null && scope == _globalScope && method.Name is "__gflat_alloc" or "__gflat_free")
                        throw new TypeCheckException("Legacy allocator hooks are no longer supported; replace Memory::Allocate and Memory::Free together", method.Line);
                    if (method.StringLiteralPrefix == null) entries.Add(new(method, members, scope, owner));
                }
                else if (node is ExternDeclaration ext && owner == null && scope == _globalScope && ext.Name is "__gflat_alloc" or "__gflat_free")
                    throw new TypeCheckException("Legacy allocator hooks are no longer supported; replace Memory::Allocate and Memory::Free together", ext.Line);
                else if (node is NamespaceDeclaration ns) Collect(ns.Members, _namespaceScopes[ns], null);
                else if (node is ClassDeclaration cls) Collect(cls.Members, scope, cls);
                else if (node is StructDeclaration str) Collect(str.Members, scope, str);
                else if (node is InterfaceDeclaration iface) Collect(iface.Members, scope, iface);
            }
        }
    }

    private string SignatureType(TypeExpression type, MethodDeclaration method)
    {
        using var context = SourceContext.Enter(method.Span);
        if (type.IsReadOnlyValue)
            return "readonly(" + SignatureType(type.WithReadOnlyValue(false), method) + ")";
        if (type is NamedTypeExpression { IsResolvedNominal: true } nominal) return nominal.Name;
        if (type is NamedTypeExpression generic && generic.Namespace == null)
        {
            int index = method.GenericParameters.FindIndex(p => p.Name == generic.Name);
            if (index >= 0) return "$type" + index;
        }
        // Preserve generic shapes without instantiating them during symbol selection.
        if (type is NamedTypeExpression { TypeArguments.Count: > 0 } applied)
            return (ResolveTypeIdentity(applied.Name, applied.Namespace) ?? applied.Namespace + "::" + applied.Name) + "<" + string.Join(",", applied.TypeArguments.Select(t => SignatureType(t, method))) + ">";
        // Resolve aliases without instantiating generic pointees during this pre-body pass.
        if (type is NamedTypeExpression or NestedTypeExpression)
        {
            TypeExpression resolved = ResolveAlias(type);
            if (!ReferenceEquals(type, resolved)) return SignatureType(resolved, method);
        }
        return type switch
        {
            NamedTypeExpression named => ResolveTypeIdentity(named.Name, named.Namespace) ?? (named.Namespace == null ? "" : named.Namespace + "::") + named.Name,
            ReferenceTypeExpression r => "ref " + SignatureType(r.Inner, method),
            PointerTypeExpression p => $"{(p.IsReadOnly ? "readonly " : "")}{SignatureType(p.Inner, method)}*{(p.IsNullable ? "?" : "")}",
            ManagedTypeExpression p => $"{(p.IsReadOnly ? "readonly " : "")}{SignatureType(p.Inner, method)}^{SignatureType(ResolveCollectorType(p.Collector, p.Line), method)}{(p.IsNullable ? "?" : "")}",
            ArrayTypeExpression a => SignatureType(a.ElementType, method) + $"[{((ArrayTypeExpression)ResolveAlias(new ArrayTypeExpression(Int, a.Size, a.Line, a.SizeExpression))).Size}]",
            FunctionPointerTypeExpression f => SignatureType(f.ReturnType, method) + "(" + string.Join(",", f.ParameterTypes.Select(t => SignatureType(t, method))) + $"){(f.IsSignature ? "" : f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => TypeName(type)
        };
    }

    private string ParameterKey(MethodDeclaration method) => method.GenericParameters.Count + ":" +
        string.Join(",", method.Parameters.Select(p => SignatureType(p.Type, method)));

    private bool ParametersMatch(MethodDeclaration first, MethodDeclaration second) => ParameterKey(first) == ParameterKey(second);

    private bool ContractMatches(MethodDeclaration first, MethodDeclaration second) =>
        SignatureType(first.ReturnType, first) == SignatureType(second.ReturnType, second) &&
        first.Accessibility == second.Accessibility && first.IsStatic == second.IsStatic &&
        first.IsVirtual == second.IsVirtual && first.IsOverride == second.IsOverride &&
        first.IsConst == second.IsConst && first.IsReadOnly == second.IsReadOnly && first.IsNoThrow == second.IsNoThrow &&
        first.Parameters.Select(p => p.IsConst).SequenceEqual(second.Parameters.Select(p => p.IsConst)) &&
        first.Parameters.Select(p => p.IsExtensionReceiver).SequenceEqual(second.Parameters.Select(p => p.IsExtensionReceiver)) &&
        first.GenericParameters.Select(p => p.Constraint == null ? "" : SignatureType(p.Constraint, first))
            .SequenceEqual(second.GenericParameters.Select(p => p.Constraint == null ? "" : SignatureType(p.Constraint, second)));

    private void ValidateAllocatorPair()
    {
        if (!_globalScope.Children.TryGetValue("Memory", out var scope)) return;
        var allocate = GetMemoryFunction("Allocate");
        var free = GetMemoryFunction("Free");
        if (allocate == null || free == null)
        {
            if (allocate?.ImplementationKind == FunctionImplementationKind.Replace || free?.ImplementationKind == FunctionImplementationKind.Replace)
                throw new TypeCheckException("Replace Memory::Allocate and Memory::Free together", (allocate ?? free)!.Span);
            return;
        }
        if ((allocate.ImplementationKind == FunctionImplementationKind.Replace) != (free.ImplementationKind == FunctionImplementationKind.Replace))
            throw new TypeCheckException("Replace Memory::Allocate and Memory::Free together", allocate.Line);
    }

    private void RequireMemoryFunction(string name, AstNode use)
    {
        if (!HasMemoryFunction(name))
            throw new TypeCheckException($"This operation requires Memory::{name}; include a memory implementation or provide the function explicitly", use.Span);
        if (GetMemoryFunction(name) == null)
            throw new TypeCheckException($"Memory::{name} must provide the public non-generic signature " +
                (name == "Allocate" ? "void*? Allocate(nuint bytes)" : "void Free(void* storage)"), use.Span);
    }

    public bool HasMemoryFunction(string name) => _globalScope.Children.TryGetValue("Memory", out var scope) && scope.Functions.ContainsKey(name);
    public MethodDeclaration? GetMemoryFunction(string name)
    {
        if (!_globalScope.Children.TryGetValue("Memory", out var scope) || !scope.Functions.TryGetValue(name, out var first)) return null;
        var family = overloadFamilies.TryGetValue(first, out var overloads) ? overloads : [first];
        return family.FirstOrDefault(method => !method.IsGeneric && method.Accessibility == TokenKind.Public && method.Parameters.Count == 1 &&
            (name == "Allocate"
                ? ResolveAlias(method.Parameters[0].Type) is NamedTypeExpression { Name: "nuint" } &&
                    ResolveAlias(method.ReturnType) is PointerTypeExpression { IsReadOnly: false, Inner: NamedTypeExpression { Name: "void" } }
                : ResolveAlias(method.Parameters[0].Type) is PointerTypeExpression { IsReadOnly: false, Inner: NamedTypeExpression { Name: "void" } } &&
                    ResolveAlias(method.ReturnType) is NamedTypeExpression { Name: "void" }));
    }
    public MethodDeclaration? GetUnhandledExceptionHook()
    {
        if (!_globalScope.Children.TryGetValue("Exceptions", out var scope) ||
            !scope.Functions.TryGetValue("Unhandled", out var first)) return null;
        var family = overloadFamilies.TryGetValue(first, out var overloads) ? overloads : [first];
        return family.FirstOrDefault(method => !method.IsGeneric && method.Body != null &&
            method.Accessibility == TokenKind.Public && !method.IsStatic && !method.IsReadOnly && !method.IsConst &&
            !method.IsVirtual && !method.IsOverride && !method.IsAbstract &&
            ResolveAlias(method.ReturnType) is NamedTypeExpression { Name: "void", IsReadOnlyValue: false } &&
            method.Parameters is [{ IsConst: false, IsExtensionReceiver: false } parameter] &&
            ResolveAlias(parameter.Type) is PointerTypeExpression
            {
                IsNullable: true, IsReadOnly: false, IsReadOnlyValue: false,
                Inner: NamedTypeExpression { Name: "Exception", IsReadOnlyValue: false }
            });
    }

    private void ValidateUnhandledExceptionHook()
    {
        if (!_globalScope.Children.TryGetValue("Exceptions", out var scope)) return;
        AstNode? declaration = (AstNode?)scope.Functions.GetValueOrDefault("Unhandled") ??
            (AstNode?)scope.GenericFunctions.GetValueOrDefault("Unhandled") ?? scope.Externs.GetValueOrDefault("Unhandled");
        if (declaration != null && GetUnhandledExceptionHook() == null)
            throw new TypeCheckException("Exceptions::Unhandled must provide the public non-generic signature 'void Unhandled(Exception*? error)'", declaration.Span);
    }
}
