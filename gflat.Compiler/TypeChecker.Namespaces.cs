using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<AstNode, string> declarationNamespaces = new();

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
        var previous = _currentNamespace;
        try
        {
            foreach (var (declaration, ns) in declarationNamespaces)
            {
                using var context = SourceContext.Enter(declaration.Span);
                _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
                if (declaration is ClassDeclaration cls && _classes.TryGetValue(cls.Name, out var info))
                {
                    if (info.BaseClass != null) info.BaseClass = ResolveTypeIdentity(info.BaseClass, null) ?? info.BaseClass;
                    for (int i = 0; i < info.Interfaces.Count; i++) info.Interfaces[i] = ResolveTypeIdentity(info.Interfaces[i], null) ?? info.Interfaces[i];
                }
                if (declaration is StructDeclaration str && _structs.TryGetValue(str.Name, out var structInfo))
                    for (int i = 0; i < structInfo.Interfaces.Count; i++) structInfo.Interfaces[i] = ResolveTypeIdentity(structInfo.Interfaces[i], null) ?? structInfo.Interfaces[i];
            }
        }
        finally { _currentNamespace = previous; }
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

    private void CheckSpecialization(AstNode definition, AstNode specialized)
    {
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        _currentClass = null;
        _currentStruct = null;
        string ns = declarationNamespaces.GetValueOrDefault(definition) ??
            (definition is MethodDeclaration m ? GetFunctionNamespace(m).Replace("$", "::") : "");
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        _currentNamespacePath = ns;
        declarationNamespaces[specialized] = ns;
        try { specialized.Accept(this); }
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
                        break;
                    case OperatorDeclaration op:
                        op.ReturnType = ResolveAlias(op.ReturnType);
                        foreach (var p in op.Parameters) p.Type = ResolveAlias(p.Type);
                        break;
                    case ConstructorDeclaration ctor:
                        foreach (var p in ctor.Parameters) p.Type = ResolveAlias(p.Type);
                        break;
                    case FieldDeclaration field: ResolveAlias(field.Type); break;
                }
            }
        }
    }
}
