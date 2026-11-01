using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record ReflectedLocal(string Name, AstNode Declaration, TypeExpression Type,
        Dictionary<string, TypeExpression> Scope, int? ParameterIndex);
    private readonly Dictionary<AstNode, string> reflectedDeclarationIdentities = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Dictionary<string, TypeExpression>, Dictionary<string, ReflectedLocal>> reflectedLocals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ReflectedLocal> reflectedLocalIdentities = new();
    private readonly Dictionary<Dictionary<string, TypeExpression>, Dictionary<string, AliasDeclaration>> reflectedLocalAliases = new(ReferenceEqualityComparer.Instance);

    private string ReflectedDeclarationIdentity(AstNode declaration)
    {
        if (!reflectedDeclarationIdentities.TryGetValue(declaration, out var identity))
            reflectedDeclarationIdentities[declaration] = identity = "declaration:" + reflectedDeclarationIdentities.Count;
        return identity;
    }

    private void RememberReflectedVariable(string name, TypeExpression type, AstNode declaration)
    {
        var scope = _scopes.Peek();
        type = RefineReflectionThisType(name, type);
        scope[name] = type;
        if (!reflectedLocals.TryGetValue(scope, out var declarations)) reflectedLocals[scope] = declarations = new();
        int? parameterIndex = declaration is Parameter parameter ? (_currentFunction switch {
            MethodDeclaration method => method.Parameters.IndexOf(parameter),
            ConstructorDeclaration constructor => constructor.Parameters.IndexOf(parameter),
            OperatorDeclaration op => op.Parameters.IndexOf(parameter),
            LambdaExpression lambda => lambda.Parameters.IndexOf(parameter), _ => -1
        }) : null;
        var binding = new ReflectedLocal(name, declaration, type, scope, parameterIndex is >= 0 ? parameterIndex : null);
        declarations[name] = binding;
        reflectedLocalIdentities[ReflectedDeclarationIdentity(declaration) + ":" + name] = binding;
    }

    private void RememberReflectedAlias(AliasDeclaration declaration)
    {
        if (_localAliases.Count == 0) return;
        var scope = _localAliases.Peek();
        if (!reflectedLocalAliases.TryGetValue(scope, out var declarations)) reflectedLocalAliases[scope] = declarations = new();
        declarations[declaration.Name] = declaration;
    }

    private static string ReflectionNamespacePath(NamespaceScope scope)
        => scope.Parent == null ? "" : (ReflectionNamespacePath(scope.Parent) is { Length: > 0 } prefix ? prefix + "::" : "") +
            scope.Parent.Children.First(pair => ReferenceEquals(pair.Value, scope)).Key;

    private AliasDeclaration? ReflectedNamespaceAlias(NamespaceScope scope, string name, TypeExpression target)
        => declarationNamespaces.Keys.OfType<AliasDeclaration>().FirstOrDefault(alias =>
            alias.ImplementationKind != FunctionImplementationKind.Replace && alias.Name == name &&
            ReferenceEquals(alias.TargetType, target) && DeclarationNamespace(alias) == ReflectionNamespacePath(scope));

    private AliasDeclaration? FindReflectedAlias(string name, NamespaceScope? qualified)
    {
        if (qualified != null)
            return qualified.Aliases.TryGetValue(name, out var target) ? ReflectedNamespaceAlias(qualified, name, target) : null;
        foreach (var scope in _localAliases)
            if (scope.TryGetValue(name, out _))
                return reflectedLocalAliases.TryGetValue(scope, out var declarations) ? declarations.GetValueOrDefault(name) : null;
        for (var scope = _currentNamespace; scope != null; scope = scope.Parent)
        {
            if (scope.Aliases.TryGetValue(name, out var target)) return ReflectedNamespaceAlias(scope, name, target);
            if (scope.TypeNames.ContainsKey(name)) return null;
        }
        AliasDeclaration? found = null;
        foreach (string import in CurrentUsings.Distinct())
        {
            var scope = ResolveNamespaceByName(import, _currentNamespace);
            if (scope == null || !scope.Aliases.TryGetValue(name, out var target)) continue;
            var candidate = ReflectedNamespaceAlias(scope, name, target);
            if (found != null && !ReferenceEquals(candidate, found))
                throw new TypeCheckException($"Alias '{name}' is ambiguous between namespaces", SourceContext.Current ?? SourceContext.ForLine(1));
            found = candidate;
        }
        return found;
    }

    private bool TryReflectDeclaration(AstNode target, out ConstValue.Reflection? reflection)
    {
        reflection = null;
        if (!ReflectionDeclarationName(target, out string name, out var qualified)) return false;
        if (qualified == null)
        {
            foreach (var scope in _scopes)
                if (scope.ContainsKey(name))
                {
                    if (!reflectedLocals.TryGetValue(scope, out var declarations) || !declarations.TryGetValue(name, out var binding))
                        throw new TypeCheckException($"Variable '{name}' has no reflected source declaration", target.Span);
                    TypeExpression type = binding.Declaration is Parameter parameter ? parameter.Type : binding.Type;
                    reflection = new(ReflectedDeclarationIdentity(binding.Declaration) + ":" + name,
                        binding.Declaration is Parameter ? "Parameter" : "Variable", name, type, binding.Declaration,
                        Value: binding.ParameterIndex is { } position ? new ConstValue.UInteger((ulong)position) : null);
                    return true;
                }
            if (FindStaticStorage(name) is { } stored)
            {
                reflection = ReflectStoredDeclaration(stored);
                return true;
            }
            if (!IsStaticFunctionContext)
            {
                if (_currentStruct?.FieldDeclarationsByName.GetValueOrDefault(name) is { } structField)
                {
                    reflection = ReflectField(NamedTypeExpression.Resolved(_currentStruct.Name, target.Line), structField);
                    return true;
                }
                if (_currentClass?.FieldDeclarationsByName.GetValueOrDefault(name) is { } classField)
                {
                    string owner = _currentClass.Fields.First(field => field.Name == name).DeclaringClass;
                    reflection = ReflectField(NamedTypeExpression.Resolved(owner, target.Line), classField);
                    return true;
                }
            }
        }
        else if (qualified.Fields.GetValueOrDefault(name) is { } field)
        {
            reflection = ReflectStoredDeclaration(field);
            return true;
        }
        if (FindReflectedAlias(name, qualified) is { } alias)
        {
            TypeExpression aliasUse = target as TypeExpression ?? new NamedTypeExpression(name,
                qualified == null ? null : "global" + (ReflectionNamespacePath(qualified) is { Length: > 0 } path ? "::" + path : ""), target.Line)
                { Span = target.Span };
            reflection = new(ReflectedDeclarationIdentity(alias), "Alias", name, ResolveAlias(aliasUse), alias);
            return true;
        }
        return false;
    }

    private bool ReflectionDeclarationName(AstNode target, out string name, out NamespaceScope? qualified)
    {
        name = "";
        qualified = null;
        switch (target)
        {
            case IdentifierExpression identifier: name = identifier.Name; return true;
            case NamedTypeExpression { TypeArguments.Count: 0 } named when !named.IsResolvedNominal && !IsPrimitive(named.Name) && !IsReflectionType(named):
                name = named.Name;
                if (named.Namespace != null) qualified = ResolveNamespaceByName(named.Namespace);
                return named.Namespace == null || qualified != null;
            case NestedTypeExpression { TypeArguments.Count: 0 } nested:
                name = nested.Member;
                if (ReflectionNamespaceQualifier(nested.Parent) is { } qualifier) qualified = ResolveNamespace(qualifier);
                return qualified != null;
            case NamespaceAccessExpression access:
                name = access.Member;
                qualified = ResolveNamespace(access.Left);
                return qualified != null;
            default: return false;
        }
    }

    private ConstValue.Reflection ReflectStoredDeclaration(FieldDeclaration field)
    {
        var storage = GetStaticStorageInfo(field);
        if (storage.Owner != null) return ReflectField(NamedTypeExpression.Resolved(storage.Owner, field.Line), field);
        using var locals = IsolateDeclarationContext();
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        _currentNamespacePath = storage.Namespace.Replace("$", "::");
        _currentNamespace = ResolveNamespaceByName("global" + (_currentNamespacePath.Length == 0 ? "" : "::" + _currentNamespacePath))
            ?? throw new InvalidOperationException("Reflected storage has no declaring namespace");
        _currentClass = null;
        _currentStruct = null;
        try { return new(ReflectedDeclarationIdentity(field), "Field", field.Name, ResolveAlias(field.Type), field); }
        finally
        {
            _currentNamespace = previousNamespace;
            _currentNamespacePath = previousPath;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
        }
    }

    private bool TryReflectDeclarationMember(ConstValue.Reflection target, string member, int line, out ConstValue? value)
    {
        value = null;
        if (target.Declaration is FieldDeclaration field && staticStorage.TryGetValue(field, out var storage) && storage.Owner == null)
        {
            if (member == "IsStatic") value = new ConstValue.Boolean(true);
            else if (member == "QualifiedName") value = new ConstValue.String(storage.Namespace.Length == 0 ? field.Name :
                storage.Namespace.Replace("$", "::") + "::" + field.Name);
            else return false;
            return true;
        }
        if (target.Declaration is VariableDeclaration variable && member == "IsConst")
        {
            value = new ConstValue.Boolean(variable.IsConst);
            return true;
        }
        if (target.Declaration is not AliasDeclaration alias) return false;
        if (member == "IsConstrained") value = new ConstValue.Boolean(alias.IsConstrained);
        else if (member == "QualifiedName") value = new ConstValue.String(DeclarationNamespace(alias) is { Length: > 0 } ns
            ? ns + "::" + alias.Name : alias.Name);
        else if (member == "Constraints")
        {
            var constraints = boundAliasConstraints.GetValueOrDefault(alias) ?? alias.Constraints;
            value = new ConstValue.Array(constraints.Select(type => (ConstValue)ReflectType(type, line)).ToList());
        }
        else return false;
        return true;
    }

    private bool TryResolveReflectedDeclarationSplice(SpliceExpression splice, ConstValue.Reflection reflection, out AstNode? resolved)
    {
        resolved = null;
        if (reflection.Kind is "Variable" or "Parameter")
        {
            if (splice.Receiver != null) throw new TypeCheckException("A local declaration splice does not take an instance receiver", splice.Span);
            if (!reflectedLocalIdentities.TryGetValue(reflection.Identity, out var binding) || !_scopes.Contains(binding.Scope))
                throw new TypeCheckException($"Reflected variable '{reflection.Name}' is outside its declaration's scope", splice.Span);
            var current = _scopes.FirstOrDefault(scope => scope.ContainsKey(binding.Name));
            if (!ReferenceEquals(current, binding.Scope))
                throw new TypeCheckException($"Reflected variable '{reflection.Name}' is hidden in this scope", splice.Span);
            resolved = new IdentifierExpression(binding.Name, splice.Line) { Span = splice.Span };
            return true;
        }
        if (reflection.Declaration is FieldDeclaration field && staticStorage.TryGetValue(field, out var storage) && storage.Owner == null)
        {
            if (splice.Receiver != null) throw new TypeCheckException("A global field splice does not take an instance receiver", splice.Span);
            AstNode owner = new GlobalExpression(splice.Line);
            foreach (string part in storage.Namespace.Replace("$", "::").Split("::", StringSplitOptions.RemoveEmptyEntries))
                owner = new NamespaceAccessExpression(owner, part, splice.Line);
            resolved = new NamespaceAccessExpression(owner, field.Name, splice.Line) { Span = splice.Span };
            return true;
        }
        return false;
    }
}
