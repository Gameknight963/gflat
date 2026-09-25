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

        public void Execute()
        {
            // First partition base class vs interfaces for all classes
            foreach (TypeChecker.ClassInfo cls in _symbols.Classes.Values)
            {
                if (cls.BaseClass != null && !_symbols.Classes.ContainsKey(cls.BaseClass))
                {
                    string? current = cls.Name;
                    while (current != null)
                    {
                        int lastDot = current.LastIndexOf('.');
                        string enclosing = lastDot >= 0 ? current.Substring(0, lastDot) : "";
                        if (enclosing.Length > 0)
                        {
                            string candidate = $"{enclosing}.{cls.BaseClass}";
                            if (_symbols.Classes.ContainsKey(candidate))
                            {
                                cls.BaseClass = candidate;
                                break;
                            }
                            current = enclosing;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                List<string> remainingInterfaces = new();
                foreach (string item in cls.Interfaces)
                {
                    string resolvedItem = item;
                    if (!_symbols.Classes.ContainsKey(resolvedItem))
                    {
                        string? current = cls.Name;
                        while (current != null)
                        {
                            int lastDot = current.LastIndexOf('.');
                            string enclosing = lastDot >= 0 ? current.Substring(0, lastDot) : "";
                            if (enclosing.Length > 0)
                            {
                                string candidate = $"{enclosing}.{item}";
                                if (_symbols.Classes.ContainsKey(candidate))
                                {
                                    resolvedItem = candidate;
                                    break;
                                }
                                current = enclosing;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (_symbols.Classes.ContainsKey(resolvedItem))
                    {
                        if (cls.BaseClass != null)
                        {
                            throw new TypeCheckException($"Class '{cls.Name}' cannot inherit from multiple classes ('{cls.BaseClass}' and '{resolvedItem}')", cls.Line);
                        }
                        cls.BaseClass = resolvedItem;
                    }
                    else
                    {
                        remainingInterfaces.Add(item);
                    }
                }
                cls.Interfaces = remainingInterfaces;
            }

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
            if (visited.Contains(cls.Name))
            {
                return;
            }
            if (visiting.Contains(cls.Name))
            {
                throw new TypeCheckException($"Circular inheritance detected involving class '{cls.Name}'", cls.Line);
            }

            visiting.Add(cls.Name);

            if (cls.BaseClass != null)
            {
                if (!_symbols.Classes.TryGetValue(cls.BaseClass, out TypeChecker.ClassInfo? baseInfo))
                {
                    throw new TypeCheckException($"Class '{cls.Name}' inherits from unknown class '{cls.BaseClass}'", cls.Line);
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
                    throw new TypeCheckException($"Class '{cls.Name}' cannot declare field '{f.Name}' because it is already declared in a base class", f.Line);
                }
                cls.Fields.Add((f.Name, f.Type, f.Accessibility, cls.Name));
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
                        throw new TypeCheckException($"Method '{method.Name}' in class '{cls.Name}' is marked override but does not override any virtual or abstract method in a base class", method.Line);
                    }
                    MethodDeclaration baseMethod = cls.VirtualMethods[slot];
                    if (baseMethod.Throws != method.Throws)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' must match throws specification of base method", method.Line);
                    }
                    if (baseMethod.IsReadOnly && !method.IsReadOnly)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' must be marked readonly to match base method", method.Line);
                    }
                    if (!TypeChecker.TypesMatchPublic(_symbols.ResolveAlias(method.ReturnType), _symbols.ResolveAlias(baseMethod.ReturnType), _symbols))
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' has return type '{TypeChecker.TypeNamePublic(method.ReturnType)}' which does not match base method return type '{TypeChecker.TypeNamePublic(baseMethod.ReturnType)}'", method.Line);
                    }
                    if (method.Parameters.Count != baseMethod.Parameters.Count)
                    {
                        throw new TypeCheckException($"Overriding method '{method.Name}' in class '{cls.Name}' has {method.Parameters.Count} parameters, but base method has {baseMethod.Parameters.Count}", method.Line);
                    }
                    for (int p = 0; p < method.Parameters.Count; p++)
                    {
                        if (!TypeChecker.TypesMatchPublic(_symbols.ResolveAlias(method.Parameters[p].Type), _symbols.ResolveAlias(baseMethod.Parameters[p].Type), _symbols) || method.Parameters[p].IsConst != baseMethod.Parameters[p].IsConst)
                        {
                            throw new TypeCheckException($"Parameter '{method.Parameters[p].Name}' of overriding method '{method.Name}' has type '{TypeChecker.TypeNamePublic(method.Parameters[p].Type)}' which does not match base parameter type '{TypeChecker.TypeNamePublic(baseMethod.Parameters[p].Type)}'", method.Parameters[p].Line);
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
                        throw new TypeCheckException($"Method '{method.Name}' in class '{cls.Name}' hides base virtual method without 'override' keyword", method.Line);
                    }
                    int slot = cls.VirtualMethods.Count;
                    cls.VirtualMethods.Add(method);
                    cls.VTableSlots[method.Name] = slot;
                    cls.Methods[method.Name] = (method, cls.Name);
                }
            }

            // Handle virtual destructor slot
            bool isVirtualDtor = cls.Destructor?.IsVirtual == true || (cls.Destructor != null && cls.VirtualMethods.Count > 0) || cls.DestructorSlot >= 0;
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
                        throw new TypeCheckException($"Class '{cls.Name}' must implement abstract method '{declaringClass}.{vm.Name}' or be declared abstract", cls.Line);
                    }
                }
            }

            visiting.Remove(cls.Name);
            visited.Add(cls.Name);
        }
    }
}
