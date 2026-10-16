using gflat.ast;
using gflat.CompileExceptions;
using gflat.semantics;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<AstNode, string> declarationNamespaces = new();
    public string? DeclarationNamespace(AstNode node) => declarationNamespaces.GetValueOrDefault(node);

    private void QualifyTypeDeclarations(CompilationUnit unit)
    {
        Walk(unit.Members, "", null);
        foreach (var ns in unit.Namespaces) Walk(ns.Members, ns.Name, null);
        void Walk(List<AstNode> members, string ns, string? owner)
        {
            foreach (var node in members)
            {
                declarationNamespaces[node] = ns;
                if (node is NamespaceDeclaration child) { Walk(child.Members, ns.Length > 0 ? ns + "::" + child.Name : child.Name, null); continue; }
                string SourceIdentity(string name) => owner != null ? DisplayName(owner) + "::" + name : ns.Length > 0 ? ns + "::" + name : name;
                string Identity(string name) => owner != null ? owner + "." + name : ns.Length > 0 ? ns.Replace("::", "$") + "$" + name : name;
                if (node is ClassDeclaration cls)
                {
                    cls.Name = Identity(cls.Name);
                    _displayNames[cls.Name] = SourceIdentity(cls.SourceName);
                    RenameConstructors(cls.Members, cls.SourceName, cls.Name);
                    Walk(cls.Members, ns, cls.Name);
                }
                else if (node is StructDeclaration str)
                {
                    str.Name = Identity(str.Name);
                    _displayNames[str.Name] = SourceIdentity(str.SourceName);
                    RenameConstructors(str.Members, str.SourceName, str.Name);
                    Walk(str.Members, ns, str.Name);
                }
                else if (node is InterfaceDeclaration iface)
                {
                    iface.Name = Identity(iface.Name);
                    _displayNames[iface.Name] = SourceIdentity(iface.SourceName);
                }
                else if (node is EnumDeclaration enumeration)
                {
                    enumeration.Name = Identity(enumeration.Name);
                    _displayNames[enumeration.Name] = SourceIdentity(enumeration.SourceName);
                }
            }
        }
    }

    private static void RenameConstructors(List<AstNode> members, string original, string identity)
    {
        foreach (var member in members)
        {
            if (member is ConstructorDeclaration ctor && ctor.Name == original) ctor.Name = identity;
            if (member is DestructorDeclaration dtor && dtor.Name == original) dtor.Name = identity;
        }
    }

    private string? ResolveTypeIdentity(string name, string? ns)
    {
        if (name.Contains('$')) return name;
        int separator = name.LastIndexOf("::", StringComparison.Ordinal);
        if (separator >= 0) { ns = name[..separator]; name = name[(separator + 2)..]; }
        if (ns != null)
            return ResolveNamespaceByName(ns)?.TypeNames.GetValueOrDefault(name);
        foreach (var local in _localEnums)
            if (local.TryGetValue(name, out var enumeration)) return enumeration.Name;
        for (var scope = _currentNamespace; scope != null; scope = scope.Parent)
            if (scope.TypeNames.TryGetValue(name, out var identity)) return identity;
        string? found = null;
        foreach (var imported in CurrentUsings)
        {
            string? candidate = ResolveNamespaceByName(imported)?.TypeNames.GetValueOrDefault(name);
            if (candidate == null) continue;
            if (found != null && found != candidate) throw new TypeCheckException($"Type '{name}' is ambiguous between namespaces", 0);
            found = candidate;
        }
        return found;
    }

    private void ResolveDeclaredBaseTypes(CompilationUnit unit)
    {
        foreach (var (declaration, ns) in declarationNamespaces.ToArray())
            ResolveDeclaredBaseTypes(declaration, ns);
    }

    private void ResolveDeclaredBaseTypes(AstNode declaration, string ns)
    {
        var classInfo = declaration is ClassDeclaration c ? _classes.GetValueOrDefault(c.Name) : null;
        var structInfo = declaration is StructDeclaration s ? _structs.GetValueOrDefault(s.Name) : null;
        if (structInfo == null && (classInfo == null || classInfo.HierarchyResolved)) return;
        using var source = SourceContext.Enter(declaration.Span);
        using var locals = IsolateDeclarationContext();
        var previousNamespace = _currentNamespace; var previousPath = _currentNamespacePath;
        var previousClass = _currentClass; var previousStruct = _currentStruct;
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        _currentNamespacePath = ns;
        _currentClass = classInfo;
        _currentStruct = structInfo;
        try
        {
            if (_currentClass is { HierarchyResolved: false } cls)
            {
                string? parent = cls.BaseClass == null ? null : ResolveDeclaredBaseName(cls.BaseClass);
                var interfaces = cls.Interfaces.Select(ResolveDeclaredBaseName).ToList();
                cls.BaseClass = parent;
                cls.Interfaces = interfaces;
            }
            if (_currentStruct is { } str)
                str.Interfaces = str.Interfaces.Select(ResolveDeclaredBaseName).ToList();
        }
        finally
        {
            _currentNamespace = previousNamespace; _currentNamespacePath = previousPath;
            _currentClass = previousClass; _currentStruct = previousStruct;
        }
    }

    private string ResolveDeclaredBaseName(string name)
    {
        bool Exists(string candidate) => _classes.ContainsKey(candidate) || _structs.ContainsKey(candidate) || _interfaces.ContainsKey(candidate);
        string nested = name.Replace("::", ".");
        string? owner = _currentClass?.Name ?? _currentStruct?.Name;
        while (owner != null)
        {
            string candidate = owner + "." + nested;
            if (Exists(candidate)) return candidate;
            int separator = owner.LastIndexOf('.');
            owner = separator < 0 ? null : owner[..separator];
        }
        if (ResolveTypeIdentity(name, null) is { } identity) return identity;
        if (Exists(nested)) return nested;
        // A qualified nested type may have a namespace prefix and then type
        // scopes; those use distinct internal separators.
        for (int separator = name.LastIndexOf("::", StringComparison.Ordinal); separator >= 0;
            separator = name.LastIndexOf("::", separator - 1, StringComparison.Ordinal))
        {
            string prefix = name[..separator];
            if (ResolveNamespaceByName(prefix) is not { } scope) continue;
            var segments = new List<string>();
            for (var current = scope; current.Parent is { } parent; current = parent)
                segments.Add(parent.Children.First(child => child.Value == current).Key);
            string path = string.Join("$", segments.AsEnumerable().Reverse());
            string candidate = path + "$" + name[(separator + 2)..].Replace("::", ".");
            if (Exists(candidate)) return candidate;
        }
        return name;
    }

    private void ResolveSpecializedBaseTypes(AstNode definition, AstNode specialized)
    {
        string ns = declarationNamespaces.GetValueOrDefault(definition) ?? "";
        var classes = new List<ClassInfo>();
        Walk(specialized);
        var hierarchy = new HierarchyResolutionPass(_symbols);
        foreach (var cls in classes) hierarchy.ResolveBaseTypes(cls);
        foreach (var cls in classes) hierarchy.ResolveHierarchy(cls);

        void Walk(AstNode declaration)
        {
            declarationNamespaces[declaration] = ns;
            ResolveDeclaredBaseTypes(declaration, ns);
            if (declaration is ClassDeclaration cls && _classes.TryGetValue(cls.Name, out var info)) classes.Add(info);
            IEnumerable<AstNode> members = declaration switch {
                ClassDeclaration childClass => childClass.Members, StructDeclaration str => str.Members, _ => []
            };
            foreach (var member in members) Walk(member);
        }
    }

    private MethodDeclaration? ResolveGenericFunction(string name)
    {
        for (var scope = _currentNamespace; scope != null; scope = scope.Parent)
            if (scope.GenericFunctions.TryGetValue(name, out var method)) return method;
        MethodDeclaration? found = null;
        foreach (var imported in CurrentUsings)
        {
            var candidate = ResolveNamespaceByName(imported)?.GenericFunctions.GetValueOrDefault(name);
            if (candidate == null) continue;
            if (found != null && found != candidate) throw new TypeCheckException($"Generic function '{name}' is ambiguous between namespaces", 0);
            found = candidate;
        }
        return found;
    }

    private void AnalyzeSafely(Action action)
    {
        int scopes = _scopes.Count;
        try { action(); }
        catch (TypeCheckException error) when (_recoverErrors)
        {
            nullableFacts.Clear();
            nullableConditions.Clear();
            _diagnostics.Report(gflat.diagnostics.DiagnosticRules.GF1000_GeneralTypeError, error.Span, error.Description);
        }
        finally { if (_recoverErrors) while (_scopes.Count > scopes) PopScope(); }
    }

    private void CheckSpecialization(AstNode definition, AstNode specialized)
    {
        using var nullableScope = IsolateNullableFlow();
        using var exceptionScope = IsolateExceptionHandlers();
        using var declarationContext = IsolateDeclarationContext();
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        _currentClass = null;
        _currentStruct = null;
        if (definition is MethodDeclaration method && TryGetMethodOwner(method, out string owner, out bool isClass))
        {
            if (isClass) _currentClass = GetClass(owner); else _currentStruct = GetStruct(owner);
        }
        string ns = declarationNamespaces.GetValueOrDefault(definition) ??
            (definition is MethodDeclaration m ? GetFunctionNamespace(m).Replace("$", "::") : "");
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        _currentNamespacePath = ns;
        declarationNamespaces[specialized] = ns;
        try { AnalyzeSafely(() => specialized.Accept(this)); }
        finally { _currentNamespace = previousNamespace; _currentNamespacePath = previousPath; _currentClass = previousClass; _currentStruct = previousStruct; }
    }

    private void ResolveDeclarationSignatures(CompilationUnit unit)
    {
        var previous = _currentNamespace;
        try
        {
            Walk(unit.Members, _globalScope);
            foreach (var ns in unit.Namespaces) Walk(ns.Members, _namespaceScopes[ns]);
        }
        finally { _currentNamespace = previous; }
        void Walk(List<AstNode> members, NamespaceScope scope)
        {
            foreach (var member in members.ToArray())
            {
                _currentNamespace = scope;
                using var context = SourceContext.Enter(member.Span);
                switch (member)
                {
                    case NamespaceDeclaration ns: Walk(ns.Members, _namespaceScopes[ns]); break;
                    case ClassDeclaration cls when !cls.IsGeneric:
                    {
                        var previousClass = _currentClass;
                        var previousStruct = _currentStruct;
                        _currentClass = GetClass(cls.Name);
                        _currentStruct = null;
                        try { Walk(cls.Members, scope); }
                        finally { _currentClass = previousClass; _currentStruct = previousStruct; }
                        break;
                    }
                    case StructDeclaration str when !str.IsGeneric:
                    {
                        var previousClass = _currentClass;
                        var previousStruct = _currentStruct;
                        _currentClass = null;
                        _currentStruct = GetStruct(str.Name);
                        try { Walk(str.Members, scope); }
                        finally { _currentClass = previousClass; _currentStruct = previousStruct; }
                        break;
                    }
                    case InterfaceDeclaration iface: Walk(iface.Members, scope); break;
                    case MethodDeclaration method when !method.IsGeneric:
                        method.ReturnType = ResolveAlias(method.ReturnType);
                        foreach (var p in method.Parameters) p.Type = ResolveAlias(p.Type);
                        RegisterParameterDefaults(method, method.Parameters);
                        break;
                    case OperatorDeclaration op:
                        op.ReturnType = ResolveAlias(op.ReturnType);
                        foreach (var p in op.Parameters) p.Type = ResolveAlias(p.Type);
                        RegisterParameterDefaults(op, op.Parameters);
                        break;
                    case ConstructorDeclaration ctor:
                        foreach (var p in ctor.Parameters) p.Type = ResolveAlias(p.Type);
                        RegisterParameterDefaults(ctor, ctor.Parameters);
                        break;
                    case ExternDeclaration external:
                        external.ReturnType = ResolveAlias(external.ReturnType);
                        foreach (var p in external.Parameters) p.Type = ResolveAlias(p.Type);
                        RegisterParameterDefaults(external, external.Parameters);
                        break;
                    case FieldDeclaration field:
                        PrepareArrayFieldShape(field);
                        defaultConstantContexts[field] = new(scope, declarationNamespaces.GetValueOrDefault(field) ?? "", _currentClass, _currentStruct);
                        break;
                }
            }
        }
    }
}
