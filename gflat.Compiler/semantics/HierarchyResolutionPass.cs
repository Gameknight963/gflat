using System;
using System.Collections.Generic;
using System.Linq;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.symbols;

namespace gflat.semantics
{
    public class HierarchyResolutionPass
    {
        private readonly SymbolTable _symbols;
        private static readonly TypeExpression Void = new NamedTypeExpression("void", null, 0);

        public HierarchyResolutionPass(SymbolTable symbols)
        {
            _symbols = symbols;
        }

        public void ResolveBaseTypes()
        {
            foreach (TypeChecker.ClassInfo cls in _symbols.Classes.Values)
                ResolveBaseTypes(cls);

            // Generic bodies can inspect these edges before layout resolution.
            // Reject cycles now so their inheritance walks cannot loop forever.
            HashSet<string> completed = new();
            foreach (var cls in _symbols.Classes.Values)
            {
                HashSet<string> path = new();
                for (TypeChecker.ClassInfo? current = cls; current != null && !completed.Contains(current.Name);
                    current = current.BaseClass == null ? null : _symbols.Classes.GetValueOrDefault(current.BaseClass))
                {
                    if (!path.Add(current.Name))
                        throw new TypeCheckException($"Circular inheritance detected involving class '{_symbols.DisplayName(current.Name)}'", current.Line);
                }
                completed.UnionWith(path);
            }
        }

        public void ResolveBaseTypes(TypeChecker.ClassInfo cls)
        {
            if (cls.HierarchyResolved) return;
            string? baseClass = cls.BaseClass == null ? null : ResolveClassName(cls.BaseClass);
            List<string> interfaces = new();
            foreach (string item in cls.Interfaces)
            {
                string candidate = ResolveClassName(item);
                if (!_symbols.Classes.ContainsKey(candidate))
                {
                    interfaces.Add(item);
                    continue;
                }
                if (baseClass != null)
                    throw new TypeCheckException($"Class '{_symbols.DisplayName(cls.Name)}' cannot inherit from multiple classes ('{_symbols.DisplayName(baseClass)}' and '{_symbols.DisplayName(candidate)}')", cls.Span);
                baseClass = candidate;
            }
            if (baseClass != null && _symbols.Classes.TryGetValue(baseClass, out var parent) && parent.IsStatic)
                throw new TypeCheckException($"Cannot inherit from static class '{_symbols.DisplayName(baseClass)}'", cls.Span);
            // Publish only validated edges so a failed partition can be retried.
            cls.BaseClass = baseClass;
            cls.Interfaces = interfaces;

            string ResolveClassName(string name)
            {
                if (_symbols.Classes.ContainsKey(name)) return name;
                string owner = cls.Name;
                for (int separator = owner.LastIndexOf('.'); separator >= 0; separator = owner.LastIndexOf('.'))
                {
                    owner = owner[..separator];
                    string nested = owner + "." + name;
                    if (_symbols.Classes.ContainsKey(nested)) return nested;
                }
                return name;
            }
        }

        public void Execute()
        {
            ResolveBaseTypes();
            HashSet<string> visited = new();
            HashSet<string> visiting = new();

            foreach (TypeChecker.ClassInfo cls in _symbols.Classes.Values)
            {
                ResolveClassHierarchy(cls, visiting, visited);
            }
        }

        public void ResolveHierarchy(TypeChecker.ClassInfo cls)
        {
            using var context = SourceContext.Enter(cls.Span);
            ResolveClassHierarchy(cls, new HashSet<string>(), new HashSet<string>());
        }

        private void ResolveClassHierarchy(TypeChecker.ClassInfo cls, HashSet<string> visiting, HashSet<string> visited)
        {
            if (cls.HierarchyResolved || visited.Contains(cls.Name))
            {
                return;
            }
            if (visiting.Contains(cls.Name))
            {
                throw new TypeCheckException($"Circular inheritance detected involving class '{_symbols.DisplayName(cls.Name)}'", cls.Line);
            }

            visiting.Add(cls.Name);

            var fields = cls.Fields.ToArray();
            var fieldDeclarations = cls.FieldDeclarationsByName.ToArray();
            var methods = cls.Methods.ToArray();
            var virtualMethods = cls.VirtualMethods.ToArray();
            var slots = cls.VTableSlots.ToArray();
            var interfaces = cls.Interfaces.ToArray();
            int destructorSlot = cls.DestructorSlot;
            try
            {
                ResolveClassMembers(cls, visiting, visited);
                cls.HierarchyResolved = true;
                visited.Add(cls.Name);
            }
            catch
            {
                // Editor recovery may retry an invalid hierarchy. Preserve the
                // declarations rather than leaving a partly inherited layout.
                Restore(cls.Fields, fields);
                Restore(cls.FieldDeclarationsByName, fieldDeclarations);
                Restore(cls.Methods, methods);
                Restore(cls.VirtualMethods, virtualMethods);
                Restore(cls.VTableSlots, slots);
                Restore(cls.Interfaces, interfaces);
                cls.DestructorSlot = destructorSlot;
                throw;
            }
            finally { visiting.Remove(cls.Name); }

            static void Restore<T>(ICollection<T> collection, T[] values)
            {
                collection.Clear();
                foreach (T value in values) collection.Add(value);
            }
        }

        private void ResolveClassMembers(TypeChecker.ClassInfo cls, HashSet<string> visiting, HashSet<string> visited)
        {
            if (cls.BaseClass != null)
            {
                if (!_symbols.Classes.TryGetValue(cls.BaseClass, out TypeChecker.ClassInfo? baseInfo))
                {
                    throw new TypeCheckException($"Class '{_symbols.DisplayName(cls.Name)}' inherits from unknown class '{_symbols.DisplayName(cls.BaseClass)}'", cls.Line);
                }

                ResolveClassHierarchy(baseInfo, visiting, visited);

                // Inherit base fields in prefix order
                cls.Fields.AddRange(baseInfo.Fields);
                foreach (KeyValuePair<string, FieldDeclaration> kvp in baseInfo.FieldDeclarationsByName)
                {
                    cls.FieldDeclarationsByName[kvp.Key] = kvp.Value;
                }

                // Inherit base vtable slots
                cls.VirtualMethods.AddRange(baseInfo.VirtualMethods);
                foreach (KeyValuePair<string, int> kvp in baseInfo.VTableSlots)
                {
                    cls.VTableSlots[kvp.Key] = kvp.Value;
                }

                // Inherit base methods (not overridden)
                foreach (KeyValuePair<string, (MethodDeclaration Method, string DeclaringClass)> kvp in baseInfo.Methods)
                {
                    if (!cls.Methods.ContainsKey(kvp.Key))
                    {
                        cls.Methods[kvp.Key] = kvp.Value;
                    }
                }

                // Inherit base interfaces
                foreach (string baseIface in baseInfo.Interfaces)
                {
                    if (!cls.Interfaces.Contains(baseIface))
                    {
                        cls.Interfaces.Add(baseIface);
                    }
                }

                // Inherit base destructor slot
                if (baseInfo.DestructorSlot >= 0)
                {
                    cls.DestructorSlot = baseInfo.DestructorSlot;
                }
            }

            // Append cls's own declared fields
            foreach (FieldDeclaration f in cls.FieldDeclarations)
            {
                if (cls.FieldIndex(f.Name) >= 0)
                {
                    throw new TypeCheckException($"Class '{_symbols.DisplayName(cls.Name)}' cannot declare field '{f.Name}' because it is already declared in a base class", f.Line);
                }
                if (!f.IsStatic) cls.Fields.Add((f.Name, f.Type, f.Accessibility, cls.Name));
                cls.FieldDeclarationsByName[f.Name] = f;
            }

            // Process methods: override, virtual, abstract, normal
            foreach (KeyValuePair<string, (MethodDeclaration Method, string DeclaringClass)> kvp in cls.Methods.Where(m => m.Value.DeclaringClass == cls.Name).ToList())
            {
                MethodDeclaration method = kvp.Value.Method;
                if (method.IsOverride)
                {
                    if (!cls.VTableSlots.TryGetValue(method.Name, out int slot))
                    {
                        throw new TypeCheckException($"Method '{method.Name}' in class '{_symbols.DisplayName(cls.Name)}' is marked override but does not override any virtual or abstract method in a base class", method.Line);
                    }
                    MethodDeclaration baseMethod = cls.VirtualMethods[slot];
                    if (baseMethod.IsNoThrow != method.IsNoThrow)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{_symbols.DisplayName(cls.Name)}' must match nothrow specification of base method", method.Line);
                    }
                    if (baseMethod.IsReadOnly && !method.IsReadOnly)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{_symbols.DisplayName(cls.Name)}' must be marked readonly to match base method", method.Line);
                    }
                    var returnType = _symbols.ResolveAlias(method.ReturnType);
                    var baseReturnType = _symbols.ResolveAlias(baseMethod.ReturnType);
                    if (!TypeChecker.TypesMatchPublic(returnType, baseReturnType, _symbols))
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{_symbols.DisplayName(cls.Name)}' has return type '{_symbols.DisplayTypeName(returnType)}' which does not match base method return type '{_symbols.DisplayTypeName(baseReturnType)}'", method.Line);
                    }
                    if (method.Parameters.Count != baseMethod.Parameters.Count)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{_symbols.DisplayName(cls.Name)}' has {method.Parameters.Count} parameters, but base method has {baseMethod.Parameters.Count}", method.Line);
                    }
                    for (int p = 0; p < method.Parameters.Count; p++)
                    {
                        var parameterType = _symbols.ResolveAlias(method.Parameters[p].Type);
                        var baseParameterType = _symbols.ResolveAlias(baseMethod.Parameters[p].Type);
                        if (!TypeChecker.TypesMatchPublic(parameterType, baseParameterType, _symbols) || method.Parameters[p].IsConst != baseMethod.Parameters[p].IsConst)
                        {
                            throw new TypeCheckException($"Parameter '{method.Parameters[p].Name}' of overriding method '{method.Name}' has type '{_symbols.DisplayTypeName(parameterType)}' which does not match base parameter type '{_symbols.DisplayTypeName(baseParameterType)}'", method.Parameters[p].Line);
                        }
                    }
                    if (baseMethod.Accessibility == TokenKind.Public && method.Accessibility != TokenKind.Public)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' cannot reduce accessibility of public base method", method.Line);
                    }
                    if (baseMethod.Accessibility == TokenKind.Protected && method.Accessibility == TokenKind.Private)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' cannot reduce accessibility of protected base method", method.Line);
                    }

                    cls.VirtualMethods[slot] = method;
                    cls.Methods[method.Name] = (method, cls.Name);
                }
                else if (method.IsVirtual || method.IsAbstract)
                {
                    if (cls.VTableSlots.ContainsKey(method.Name))
                    {
                        throw new TypeCheckException($"Method '{method.Name}' in class '{_symbols.DisplayName(cls.Name)}' hides base virtual method without 'override' keyword", method.Line);
                    }
                    int slot = cls.VirtualMethods.Count;
                    cls.VirtualMethods.Add(method);
                    cls.VTableSlots[method.Name] = slot;
                    cls.Methods[method.Name] = (method, cls.Name);
                }
            }

            // Handle virtual destructor slot
            bool isVirtualDtor = true; // Every class view preserves destruction of the dynamic type.
            if (isVirtualDtor)
            {
                if (cls.DestructorSlot < 0)
                {
                    cls.DestructorSlot = cls.VirtualMethods.Count;
                    cls.VTableSlots["$dtor"] = cls.DestructorSlot;
                    cls.VirtualMethods.Add(new MethodDeclaration("$dtor", Void, new List<Parameter>(), null, TokenKind.Public, false, true, false, false, cls.Destructor?.Line ?? cls.Line));
                }
                else
                {
                    cls.VirtualMethods[cls.DestructorSlot] = new MethodDeclaration("$dtor", Void, new List<Parameter>(), null, TokenKind.Public, false, false, true, false, cls.Destructor?.Line ?? cls.Line);
                }
            }

            // If concrete class, ensure all abstract methods in vtable are implemented
            if (!cls.IsAbstract)
            {
                for (int slot = 0; slot < cls.VirtualMethods.Count; slot++)
                {
                    MethodDeclaration vm = cls.VirtualMethods[slot];
                    if (vm.IsAbstract)
                    {
                        string declaringClass = cls.Methods[vm.Name].DeclaringClass;
                        throw new TypeCheckException($"Class '{_symbols.DisplayName(cls.Name)}' must implement abstract method '{_symbols.DisplayName(declaringClass)}.{vm.Name}' or be declared abstract", cls.Line);
                    }
                }
            }
        }
    }
}
