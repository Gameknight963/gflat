using System;
using System.Collections.Generic;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.symbols
{
    public class SymbolTable
    {
        public Dictionary<string, TypeChecker.ClassInfo> Classes { get; } = new();
        public Dictionary<string, TypeChecker.StructInfo> Structs { get; } = new();
        public Dictionary<string, TypeChecker.InterfaceInfo> Interfaces { get; } = new();
        public Dictionary<string, TypeChecker.EnumInfo> Enums { get; } = new();

        public Dictionary<string, ClassDeclaration> GenericClasses { get; } = new();
        public Dictionary<string, StructDeclaration> GenericStructs { get; } = new();
        public Dictionary<string, MethodDeclaration> GenericMethods { get; } = new();

        public Dictionary<NamespaceDeclaration, TypeChecker.NamespaceScope> NamespaceScopes { get; } = new();
        public TypeChecker.NamespaceScope GlobalScope { get; } = new();
        public TypeChecker.NamespaceScope CurrentNamespace { get; set; } = null!;
        public string CurrentNamespacePath { get; set; } = "";
        public HashSet<string> UsingNamespaces { get; } = new();

        public Func<string?>? EnclosingTypeNameProvider { get; set; }
        public string? CurrentEnclosingTypeName { get; set; }

        public Dictionary<MethodDeclaration, string> FunctionNamespaces { get; } = new();
        public Dictionary<OperatorDeclaration, string> OperatorNamespaces { get; } = new();

        public Stack<Dictionary<string, TypeExpression>> Scopes { get; } = new();
        public Stack<Dictionary<string, TypeExpression>> LocalAliases { get; } = new();
        public Stack<Dictionary<string, TypeChecker.EnumInfo>> LocalEnums { get; } = new();

        public SymbolTable()
        {
            CurrentNamespace = GlobalScope;
        }

        public SymbolTable(
            Dictionary<string, TypeChecker.ClassInfo> classes,
            Dictionary<string, TypeChecker.StructInfo> structs,
            Dictionary<string, TypeChecker.InterfaceInfo> interfaces,
            Dictionary<string, TypeChecker.EnumInfo> enums,
            Dictionary<string, ClassDeclaration> genericClasses,
            Dictionary<string, StructDeclaration> genericStructs,
            Dictionary<string, MethodDeclaration> genericMethods,
            Dictionary<NamespaceDeclaration, TypeChecker.NamespaceScope> namespaceScopes,
            TypeChecker.NamespaceScope globalScope,
            Dictionary<MethodDeclaration, string> functionNamespaces,
            Dictionary<OperatorDeclaration, string> operatorNamespaces,
            Stack<Dictionary<string, TypeExpression>> scopes,
            Stack<Dictionary<string, TypeExpression>> localAliases,
            Stack<Dictionary<string, TypeChecker.EnumInfo>> localEnums,
            HashSet<string> usingNamespaces)
        {
            Classes = classes;
            Structs = structs;
            Interfaces = interfaces;
            Enums = enums;
            GenericClasses = genericClasses;
            GenericStructs = genericStructs;
            GenericMethods = genericMethods;
            NamespaceScopes = namespaceScopes;
            GlobalScope = globalScope;
            CurrentNamespace = globalScope;
            FunctionNamespaces = functionNamespaces;
            OperatorNamespaces = operatorNamespaces;
            Scopes = scopes;
            LocalAliases = localAliases;
            LocalEnums = localEnums;
            UsingNamespaces = usingNamespaces;
        }

        public void Reset()
        {
            Classes.Clear();
            Structs.Clear();
            Interfaces.Clear();
            Enums.Clear();
            GenericClasses.Clear();
            GenericStructs.Clear();
            GenericMethods.Clear();
            NamespaceScopes.Clear();
            GlobalScope.Children.Clear();
            GlobalScope.Functions.Clear();
            GlobalScope.GenericFunctions.Clear();
            GlobalScope.TypeNames.Clear();
            GlobalScope.Externs.Clear();
            GlobalScope.Aliases.Clear();
            GlobalScope.Enums.Clear();
            GlobalScope.Classes.Clear();
            GlobalScope.Structs.Clear();
            GlobalScope.Interfaces.Clear();
            GlobalScope.Fields.Clear();
            CurrentNamespace = GlobalScope;
            CurrentNamespacePath = "";
            UsingNamespaces.Clear();
            FunctionNamespaces.Clear();
            OperatorNamespaces.Clear();
            Scopes.Clear();
            LocalAliases.Clear();
            LocalEnums.Clear();
        }

        public Func<TypeExpression, TypeExpression>? AliasResolver { get; set; }

        public TypeExpression ResolveAlias(TypeExpression type) =>
            AliasResolver != null ? AliasResolver(type) : ResolveAliasDirect(type);

        public TypeExpression ResolveAliasDirect(TypeExpression type)
        {
            var resolved = ResolveAliasDirectCore(type);
            return type.IsReadOnlyValue ? TypeQualifiers.ReadOnly(resolved) : resolved;
        }
        private TypeExpression ResolveAliasDirectCore(TypeExpression type)
        {
            if (type is NestedTypeExpression nested)
            {
                TypeExpression resolvedParent = ResolveAlias(nested.Parent);
                if (resolvedParent is NamedTypeExpression namedParent)
                {
                    string parentNs = namedParent.Namespace != null ? $"{namedParent.Namespace}::{namedParent.Name}" : namedParent.Name;
                    TypeChecker.NamespaceScope? ns = ResolveNamespaceByName(parentNs);
                    if (ns != null && ns.Aliases.TryGetValue(nested.Member, out TypeExpression? target))
                    {
                        return ResolveAlias(target);
                    }
                    string candidate = $"{namedParent.Name}.{nested.Member}";
                    return ResolveAlias(new NamedTypeExpression(candidate, namedParent.Namespace, nested.Line));
                }
                return type;
            }
            if (type is NamedTypeExpression named)
            {
                if (named.Namespace != null)
                {
                    TypeChecker.NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                    if (ns != null && ns.Aliases.TryGetValue(named.Name, out TypeExpression? target))
                    {
                        return ResolveAlias(target);
                    }
                }
                else
                {
                    foreach (Dictionary<string, TypeExpression> localScope in LocalAliases)
                    {
                        if (localScope.TryGetValue(named.Name, out TypeExpression? target))
                        {
                            return ResolveAlias(target);
                        }
                    }
                    TypeChecker.NamespaceScope? cur = CurrentNamespace;
                    while (cur != null)
                    {
                        if (cur.Aliases.TryGetValue(named.Name, out TypeExpression? target))
                        {
                            return ResolveAlias(target);
                        }
                        cur = cur.Parent;
                    }
                    if (GlobalScope.Aliases.TryGetValue(named.Name, out TypeExpression? gTarget))
                    {
                        return ResolveAlias(gTarget);
                    }

                    string? resolvedNested = ResolveNestedTypeName(named.Name);
                    if (resolvedNested != null && resolvedNested != named.Name)
                    {
                        return new NamedTypeExpression(resolvedNested, named.Namespace, named.Line);
                    }
                }
            }
            else if (type is PointerTypeExpression ptr)
            {
                TypeExpression resolvedInner = ResolveAlias(ptr.Inner);
                if (resolvedInner != ptr.Inner)
                {
                    return new PointerTypeExpression(resolvedInner, ptr.IsNullable, ptr.Line, isReadOnly: ptr.IsReadOnly);
                }
            }
            else if (type is ManagedTypeExpression mgd)
            {
                TypeExpression resolvedInner = ResolveAlias(mgd.Inner);
                if (resolvedInner != mgd.Inner)
                {
                    return new ManagedTypeExpression(resolvedInner, mgd.IsNullable, mgd.Line, isReadOnly: mgd.IsReadOnly);
                }
            }
            else if (type is ArrayTypeExpression arr)
            {
                TypeExpression resolvedElement = ResolveAlias(arr.ElementType);
                if (resolvedElement != arr.ElementType)
                {
                    return new ArrayTypeExpression(resolvedElement, arr.Size, arr.Line);
                }
            }
            return type;
        }

        public void PushScope()
        {
            Scopes.Push(new Dictionary<string, TypeExpression>());
        }

        public void PopScope()
        {
            if (Scopes.Count > 0)
            {
                Scopes.Pop();
            }
        }

        public void DeclareVariable(string name, TypeExpression type)
        {
            if (Scopes.Count == 0)
            {
                PushScope();
            }
            Scopes.Peek()[name] = type;
        }

        public bool TryLookupVariable(string name, out TypeExpression? type)
        {
            foreach (Dictionary<string, TypeExpression> scope in Scopes)
            {
                if (scope.TryGetValue(name, out type))
                {
                    return true;
                }
            }
            type = null;
            return false;
        }

        public TypeExpression LookupVariable(string name, int line)
        {
            if (TryLookupVariable(name, out TypeExpression? type) && type != null)
            {
                return type;
            }
            throw new TypeCheckException($"Variable '{name}' not found", line);
        }

        public TypeChecker.ClassInfo? GetClass(string name)
        {
            if (name.Contains("::"))
            {
                name = name.Replace("::", ".");
            }

            if (Classes.TryGetValue(name, out TypeChecker.ClassInfo? info))
            {
                return info;
            }

            string? nested = ResolveNestedTypeName(name);
            if (nested != null && Classes.TryGetValue(nested, out TypeChecker.ClassInfo? nestedInfo))
            {
                return nestedInfo;
            }

            return null;
        }

        public bool IsClass(string name) => GetClass(name) != null;

        public TypeChecker.StructInfo? GetStruct(string name)
        {
            if (name.Contains("::"))
            {
                name = name.Replace("::", ".");
            }

            if (Structs.TryGetValue(name, out TypeChecker.StructInfo? info))
            {
                return info;
            }

            string? nested = ResolveNestedTypeName(name);
            if (nested != null && Structs.TryGetValue(nested, out TypeChecker.StructInfo? nestedInfo))
            {
                return nestedInfo;
            }

            return null;
        }

        public TypeChecker.EnumInfo? GetEnum(string name)
        {
            if (Enums.TryGetValue(name, out TypeChecker.EnumInfo? info))
            {
                return info;
            }
            if (name.Contains("::") && Enums.TryGetValue(name.Replace("::", "."), out TypeChecker.EnumInfo? dotInfo))
            {
                return dotInfo;
            }
            return null;
        }

        public TypeChecker.InterfaceInfo? GetInterface(string name) =>
            Interfaces.TryGetValue(name, out TypeChecker.InterfaceInfo? i) ? i : null;

        public bool IsInterface(string name) => Interfaces.ContainsKey(name);

        public string? ResolveNestedTypeName(string name)
        {
            if (name.Contains("::"))
            {
                name = name.Replace("::", ".");
            }

            if (name.Contains('.'))
            {
                if (Classes.ContainsKey(name) || Structs.ContainsKey(name))
                {
                    return name;
                }
            }

            string? currentName = EnclosingTypeNameProvider?.Invoke() ?? CurrentEnclosingTypeName;
            while (currentName != null)
            {
                string candidate = $"{currentName}.{name}";
                if (Classes.ContainsKey(candidate) || Structs.ContainsKey(candidate))
                {
                    return candidate;
                }

                int lastDot = currentName.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    currentName = currentName.Substring(0, lastDot);
                }
                else
                {
                    break;
                }
            }

            if (Classes.ContainsKey(name) || Structs.ContainsKey(name))
            {
                return name;
            }

            return null;
        }

        public TypeChecker.NamespaceScope? ResolveNamespaceByName(string nsName)
        {
            if (nsName.Contains("::"))
            {
                string[] parts = nsName.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                TypeChecker.NamespaceScope? current = ResolveNamespaceByName(parts[0]);
                for (int i = 1; i < parts.Length && current != null; i++)
                {
                    if (!current.Children.TryGetValue(parts[i], out current))
                    {
                        return null;
                    }
                }
                return current;
            }

            TypeChecker.NamespaceScope? scope = CurrentNamespace;
            while (scope != null)
            {
                if (scope.Children.TryGetValue(nsName, out TypeChecker.NamespaceScope? child))
                {
                    return child;
                }
                scope = scope.Parent;
            }
            if (GlobalScope.Children.TryGetValue(nsName, out TypeChecker.NamespaceScope? gChild))
            {
                return gChild;
            }
            return null;
        }

        public TypeChecker.NamespaceScope? ResolveNamespace(AstNode node)
        {
            if (node is GlobalExpression)
            {
                return GlobalScope;
            }

            if (node is IdentifierExpression ident)
            {
                TypeChecker.NamespaceScope? scope = CurrentNamespace;
                while (scope != null)
                {
                    if (scope.Children.TryGetValue(ident.Name, out TypeChecker.NamespaceScope? child))
                    {
                        return child;
                    }
                    scope = scope.Parent;
                }
                if (GlobalScope.Children.TryGetValue(ident.Name, out TypeChecker.NamespaceScope? gChild))
                {
                    return gChild;
                }
                return null;
            }

            if (node is NamespaceAccessExpression access)
            {
                TypeChecker.NamespaceScope? parent = ResolveNamespace(access.Left);
                if (parent == null)
                {
                    return null;
                }
                parent.Children.TryGetValue(access.Member, out TypeChecker.NamespaceScope? child);
                return child;
            }

            if (node is MemberAccessExpression accessDot)
            {
                TypeChecker.NamespaceScope? parent = ResolveNamespace(accessDot.Object);
                if (parent == null)
                {
                    return null;
                }
                parent.Children.TryGetValue(accessDot.Member, out TypeChecker.NamespaceScope? child);
                return child;
            }

            return null;
        }

        public TypeChecker.EnumInfo? ResolveEnum(AstNode node)
        {
            if (node is IdentifierExpression ident)
            {
                foreach (Dictionary<string, TypeChecker.EnumInfo> localScope in LocalEnums)
                {
                    if (localScope.TryGetValue(ident.Name, out TypeChecker.EnumInfo? localInfo))
                    {
                        return localInfo;
                    }
                }
                TypeChecker.NamespaceScope? cur = CurrentNamespace;
                while (cur != null)
                {
                    if (cur.Enums.TryGetValue(ident.Name, out TypeChecker.EnumInfo? nsInfo))
                    {
                        return nsInfo;
                    }
                    cur = cur.Parent;
                }
                if (GlobalScope.Enums.TryGetValue(ident.Name, out TypeChecker.EnumInfo? gInfo))
                {
                    return gInfo;
                }
                if (Enums.TryGetValue(ident.Name, out TypeChecker.EnumInfo? fallback))
                {
                    return fallback;
                }
                return null;
            }

            if (node is NamespaceAccessExpression nsAccess)
            {
                TypeChecker.NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                if (scope != null && scope.Enums.TryGetValue(nsAccess.Member, out TypeChecker.EnumInfo? info))
                {
                    return info;
                }

                string fullPath = GetAccessPath(nsAccess);
                if (fullPath.Length > 0 && Enums.TryGetValue(fullPath, out TypeChecker.EnumInfo? pathInfo))
                {
                    return pathInfo;
                }

                return null;
            }

            if (node is MemberAccessExpression memberAccess)
            {
                TypeChecker.NamespaceScope? scope = ResolveNamespace(memberAccess.Object);
                if (scope != null && scope.Enums.TryGetValue(memberAccess.Member, out TypeChecker.EnumInfo? info))
                {
                    return info;
                }

                string fullPath = GetAccessPath(memberAccess);
                if (fullPath.Length > 0 && Enums.TryGetValue(fullPath, out TypeChecker.EnumInfo? pathInfo))
                {
                    return pathInfo;
                }

                return null;
            }

            return null;
        }

        public TypeChecker.EnumInfo? ResolveEnum(NamedTypeExpression named)
        {
            if (named.Namespace != null)
            {
                TypeChecker.NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                if (ns != null && ns.Enums.TryGetValue(named.Name, out TypeChecker.EnumInfo? info))
                {
                    return info;
                }

                if (Enums.TryGetValue($"{named.Namespace}::{named.Name}", out TypeChecker.EnumInfo? namespacedInfo))
                {
                    return namespacedInfo;
                }
            }
            else
            {
                foreach (Dictionary<string, TypeChecker.EnumInfo> localScope in LocalEnums)
                {
                    if (localScope.TryGetValue(named.Name, out TypeChecker.EnumInfo? info))
                    {
                        return info;
                    }
                }
                TypeChecker.NamespaceScope? cur = CurrentNamespace;
                while (cur != null)
                {
                    if (cur.Enums.TryGetValue(named.Name, out TypeChecker.EnumInfo? info))
                    {
                        return info;
                    }
                    cur = cur.Parent;
                }
                if (GlobalScope.Enums.TryGetValue(named.Name, out TypeChecker.EnumInfo? gInfo))
                {
                    return gInfo;
                }
            }

            return null;
        }

        public TypeChecker.InterfaceInfo? ResolveInterface(NamedTypeExpression named)
        {
            if (named.Namespace != null)
            {
                TypeChecker.NamespaceScope? ns = ResolveNamespaceByName(named.Namespace);
                if (ns != null && ns.Interfaces.TryGetValue(named.Name, out TypeChecker.InterfaceInfo? info))
                {
                    return info;
                }
            }
            else
            {
                TypeChecker.NamespaceScope? cur = CurrentNamespace;
                while (cur != null)
                {
                    if (cur.Interfaces.TryGetValue(named.Name, out TypeChecker.InterfaceInfo? info))
                    {
                        return info;
                    }
                    cur = cur.Parent;
                }
                if (GlobalScope.Interfaces.TryGetValue(named.Name, out TypeChecker.InterfaceInfo? gInfo))
                {
                    return gInfo;
                }
            }

            return null;
        }

        public MethodDeclaration? ResolveFunction(string name)
        {
            TypeChecker.NamespaceScope? scope = CurrentNamespace;
            while (scope != null)
            {
                if (scope.Functions.TryGetValue(name, out MethodDeclaration? method))
                {
                    return method;
                }
                scope = scope.Parent;
            }

            MethodDeclaration? foundInUsing = null;
            foreach (string usingNs in UsingNamespaces)
            {
                TypeChecker.NamespaceScope? nsScope = ResolveNamespaceByName(usingNs);
                if (nsScope != null && nsScope.Functions.TryGetValue(name, out MethodDeclaration? candidate))
                {
                    if (foundInUsing != null && foundInUsing != candidate)
                    {
                        throw new TypeCheckException($"Call to function '{name}' is ambiguous between namespaces", 0);
                    }
                    foundInUsing = candidate;
                }
            }
            if (foundInUsing != null)
            {
                return foundInUsing;
            }

            return null;
        }

        public ExternDeclaration? ResolveExtern(string name)
        {
            TypeChecker.NamespaceScope? scope = CurrentNamespace;
            while (scope != null)
            {
                if (scope.Externs.TryGetValue(name, out ExternDeclaration? ext))
                {
                    return ext;
                }
                scope = scope.Parent;
            }

            foreach (string usingNs in UsingNamespaces)
            {
                TypeChecker.NamespaceScope? nsScope = ResolveNamespaceByName(usingNs);
                if (nsScope != null && nsScope.Externs.TryGetValue(name, out ExternDeclaration? candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetAccessPath(AstNode node)
        {
            if (node is IdentifierExpression id)
            {
                return id.Name;
            }
            if (node is NamespaceAccessExpression ns)
            {
                string leftPath = GetAccessPath(ns.Left);
                return leftPath.Length > 0 ? $"{leftPath}::{ns.Member}" : ns.Member;
            }
            if (node is MemberAccessExpression ma)
            {
                string leftPath = GetAccessPath(ma.Object);
                return leftPath.Length > 0 ? $"{leftPath}::{ma.Member}" : ma.Member;
            }
            return "";
        }
    }
}
