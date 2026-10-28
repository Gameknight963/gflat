using System;
using System.Collections.Generic;
using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;
using gflat.symbols;

namespace gflat.semantics
{
    public class SymbolCollectionPass
    {
        private readonly SymbolTable _symbols;
        private readonly Func<AstNode, bool>? _namespaceVisible;
        private readonly Action<Action>? _analyzeSafely;
        private readonly Func<string, string> _displayName;

        public SymbolCollectionPass(SymbolTable symbols, Func<AstNode, bool>? namespaceVisible = null,
            Action<Action>? analyzeSafely = null, Func<string, string>? displayName = null)
        {
            _symbols = symbols;
            _namespaceVisible = namespaceVisible;
            _analyzeSafely = analyzeSafely;
            _displayName = displayName ?? (name => name);
        }

        public void Execute(CompilationUnit node)
        {
            _symbols.Reset();
            foreach (UsingDirective u in node.Usings)
            {
                _symbols.UsingNamespaces.Add(u.Name);
            }

            foreach (AstNode member in node.Members)
            {
                RegisterMemberInScope(member, _symbols.GlobalScope, "", _namespaceVisible?.Invoke(member) ?? true);
            }

            foreach (NamespaceDeclaration ns in node.Namespaces)
            {
                BuildNamespaceScope(ns, _symbols.GlobalScope, "");
            }
        }

        private void RegisterMemberInScope(AstNode member, TypeChecker.NamespaceScope scope, string nsPath, bool namespaceVisible = true)
        {
            using var context = SourceContext.Enter(member.Span);
            if (member is MethodDeclaration method)
            {
                _symbols.FunctionNamespaces[method] = nsPath;
                if (method.IsGeneric)
                {
                    scope.GenericFunctions[method.Name] = method;
                    _symbols.GenericMethods[nsPath.Length > 0 ? nsPath + "$" + method.Name : method.Name] = method;
                    return;
                }
                scope.Functions[method.Name] = method;
                _symbols.FunctionNamespaces[method] = nsPath;
            }
            else if (member is ExternDeclaration ext)
            {
                scope.Externs[ext.Name] = ext;
            }
            else if (member is InterfaceDeclaration iface)
            {
                if (namespaceVisible) scope.TypeNames[iface.SourceName] = iface.Name;
                if (iface.IsGeneric) { _symbols.GenericInterfaces[iface.Name] = iface; return; }
                TypeChecker.InterfaceInfo info = new TypeChecker.InterfaceInfo
                {
                    Name = iface.Name,
                    Declaration = iface,
                    IsDeclaredStatic = iface.IsStatic,
                    Namespace = nsPath,
                    Accessibility = iface.Accessibility
                };
                int slotIndex = 0;
                foreach (AstNode m in iface.Members)
                {
                    if (m is PropertyDeclaration) continue;
                    void RegisterInterfaceMember()
                    {
                        if (m is MethodDeclaration sm)
                        {
                            if (info.MethodsByName.ContainsKey(sm.Name))
                            {
                                throw new TypeCheckException($"Interface '{_displayName(iface.Name)}' already contains a method named '{sm.Name}'", sm.Line);
                            }
                            info.Methods.Add(sm);
                            info.MethodsByName[sm.Name] = sm;
                            info.MethodIndices[sm.Name] = slotIndex++;
                        }
                        else
                        {
                            throw new TypeCheckException($"Interface '{_displayName(iface.Name)}' can only contain method declarations", m.Line);
                        }
                    }
                    if (_analyzeSafely == null) RegisterInterfaceMember();
                    else _analyzeSafely(RegisterInterfaceMember);
                }
                _symbols.Interfaces[iface.Name] = info;
                if (namespaceVisible) scope.Interfaces[iface.SourceName] = info;
            }
            else if (member is StructDeclaration str)
            {
                if (namespaceVisible) scope.TypeNames[str.SourceName] = str.Name;
                if (str.IsGeneric)
                {
                    _symbols.GenericStructs[str.Name] = str;
                    return;
                }
                TypeChecker.StructInfo info = new TypeChecker.StructInfo
                {
                    Name = str.Name,
                    Declaration = str,
                    Namespace = nsPath,
                    Interfaces = new List<string>(str.Interfaces)
                };
                for (int i = 0; i < str.Members.Count; i++)
                {
                    AstNode m = str.Members[i];
                    if (m is ClassDeclaration nestedCls)
                    {
                        ClassDeclaration qualifiedCls = new ClassDeclaration(
                            nestedCls.Name.StartsWith(str.Name + ".") ? nestedCls.Name : $"{str.Name}.{nestedCls.Name}",
                            nestedCls.BaseClass,
                            nestedCls.Interfaces,
                            nestedCls.Members,
                            nestedCls.Accessibility,
                            nestedCls.IsAbstract,
                            nestedCls.Line,
                            nestedCls.Attributes,
                            nestedCls.GenericParameters
                        );
                        qualifiedCls.IsStatic = nestedCls.IsStatic;
                        qualifiedCls.BaseTypes = nestedCls.BaseTypes;
                        qualifiedCls.Span = nestedCls.Span;
                        str.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is StructDeclaration nestedStruct)
                    {
                        StructDeclaration qualifiedStruct = new StructDeclaration(
                            nestedStruct.Name.StartsWith(str.Name + ".") ? nestedStruct.Name : $"{str.Name}.{nestedStruct.Name}",
                            nestedStruct.Interfaces,
                            nestedStruct.Members,
                            nestedStruct.Accessibility,
                            nestedStruct.Line,
                            nestedStruct.Attributes,
                            nestedStruct.GenericParameters
                        );
                        qualifiedStruct.Span = nestedStruct.Span;
                        qualifiedStruct.BaseTypes = nestedStruct.BaseTypes;
                        str.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        if (!field.IsStatic) info.Fields.Add((field.Name, field.Type));
                        info.FieldDeclarations.Add(field);
                        info.FieldDeclarationsByName[field.Name] = field;
                    }
                    else if (m is MethodDeclaration sm)
                    {
                        info.Methods[sm.Name] = sm;
                        info.AllMethods.Add(sm);
                        _symbols.FunctionNamespaces[sm] = nsPath;
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                    }
                    else if (m is DestructorDeclaration dtor)
                    {
                        if (info.Destructor != null)
                        {
                            throw new TypeCheckException($"Struct '{_displayName(str.Name)}' already defines a destructor", dtor.Line);
                        }
                        if (dtor.IsVirtual)
                        {
                            throw new TypeCheckException($"Struct '{_displayName(str.Name)}' destructor cannot be virtual", dtor.Line);
                        }
                        string shortStrName = str.Name.Split('.').Last().Split('$').Last();
                        if (dtor.Name != str.Name && dtor.Name != shortStrName)
                        {
                            throw new TypeCheckException($"Destructor name '~{_displayName(dtor.Name)}' does not match struct name '{_displayName(str.Name)}'", dtor.Line);
                        }
                        info.Destructor = dtor;
                    }
                    else if (m is OperatorDeclaration op)
                    {
                        info.Operators.Add(op);
                        _symbols.OperatorNamespaces[op] = nsPath;
                    }
                    else if (m is EnumDeclaration enumDecl)
                    {
                        RegisterEnum(enumDecl, scope, nsPath.Length > 0 ? $"{nsPath}::{str.Name}" : str.Name, namespaceVisible: false);
                    }
                }
                _symbols.Structs[str.Name] = info;
                if (namespaceVisible) scope.Structs[str.SourceName] = info;
            }
            else if (member is ClassDeclaration cls)
            {
                if (namespaceVisible) scope.TypeNames[cls.SourceName] = cls.Name;
                if (cls.IsGeneric)
                {
                    _symbols.GenericClasses[cls.Name] = cls;
                    return;
                }
                TypeChecker.ClassInfo info = new TypeChecker.ClassInfo
                {
                    Name = cls.Name,
                    Declaration = cls,
                    Namespace = nsPath,
                    BaseClass = cls.BaseClass,
                    IsAbstract = cls.IsAbstract,
                    IsStatic = cls.IsStatic,
                    Accessibility = cls.Accessibility,
                    Interfaces = new List<string>(cls.Interfaces),
                    Line = cls.Line,
                    Span = cls.Span
                };

                for (int i = 0; i < cls.Members.Count; i++)
                {
                    AstNode m = cls.Members[i];
                    if (m is ClassDeclaration nestedCls)
                    {
                        ClassDeclaration qualifiedCls = new ClassDeclaration(
                            nestedCls.Name.StartsWith(cls.Name + ".") ? nestedCls.Name : $"{cls.Name}.{nestedCls.Name}",
                            nestedCls.BaseClass,
                            nestedCls.Interfaces,
                            nestedCls.Members,
                            nestedCls.Accessibility,
                            nestedCls.IsAbstract,
                            nestedCls.Line,
                            nestedCls.Attributes,
                            nestedCls.GenericParameters
                        );
                        qualifiedCls.IsStatic = nestedCls.IsStatic;
                        qualifiedCls.BaseTypes = nestedCls.BaseTypes;
                        qualifiedCls.Span = nestedCls.Span;
                        cls.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is StructDeclaration nestedStruct)
                    {
                        StructDeclaration qualifiedStruct = new StructDeclaration(
                            nestedStruct.Name.StartsWith(cls.Name + ".") ? nestedStruct.Name : $"{cls.Name}.{nestedStruct.Name}",
                            nestedStruct.Interfaces,
                            nestedStruct.Members,
                            nestedStruct.Accessibility,
                            nestedStruct.Line,
                            nestedStruct.Attributes,
                            nestedStruct.GenericParameters
                        );
                        qualifiedStruct.Span = nestedStruct.Span;
                        qualifiedStruct.BaseTypes = nestedStruct.BaseTypes;
                        cls.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath, namespaceVisible: false);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        info.FieldDeclarations.Add(field);
                    }
                    else if (m is ConstructorDeclaration ctor)
                    {
                        info.Constructors.Add(ctor);
                    }
                    else if (m is DestructorDeclaration dtor)
                    {
                        if (info.Destructor != null)
                        {
                            throw new TypeCheckException($"Class '{_displayName(cls.Name)}' already defines a destructor", dtor.Line);
                        }
                        string shortClsName = cls.Name.Contains('.') ? cls.Name.Substring(cls.Name.LastIndexOf('.') + 1) : cls.Name;
                        if (dtor.Name != cls.Name && dtor.Name != shortClsName)
                        {
                            throw new TypeCheckException($"Destructor name '~{_displayName(dtor.Name)}' does not match class name '{_displayName(cls.Name)}'", dtor.Line);
                        }
                        info.Destructor = dtor;
                    }
                    else if (m is MethodDeclaration cm)
                    {
                        if (cm.IsAbstract && !cls.IsAbstract)
                        {
                            throw new TypeCheckException($"Abstract method '{cm.Name}' can only be declared in an abstract class", cm.Line);
                        }
                        if (cm.IsAbstract && cm.Body != null)
                        {
                            throw new TypeCheckException($"Abstract method '{cm.Name}' cannot have a body", cm.Line);
                        }
                        if (!cm.IsAbstract && cm.Body == null && cm.ImplementationKind != FunctionImplementationKind.Expect)
                        {
                            throw new TypeCheckException($"Method '{cm.Name}' must declare a body unless marked abstract", cm.Line);
                        }

                        info.Methods[cm.Name] = (cm, cls.Name);
                        info.AllMethods.Add(cm);
                        _symbols.FunctionNamespaces[cm] = nsPath;
                    }
                    else if (m is EnumDeclaration enumDecl)
                    {
                        RegisterEnum(enumDecl, scope, nsPath.Length > 0 ? $"{nsPath}::{cls.Name}" : cls.Name, namespaceVisible: false);
                    }
                }

                _symbols.Classes[cls.Name] = info;
                if (namespaceVisible) scope.Classes[cls.SourceName] = info;
            }
            else if (member is NamespaceDeclaration nested)
            {
                BuildNamespaceScope(nested, scope, nsPath);
            }
            else if (member is AliasDeclaration alias)
            {
                scope.Aliases[alias.Name] = alias.TargetType;
            }
            else if (member is EnumDeclaration enumDecl)
            {
                RegisterEnum(enumDecl, scope, nsPath, namespaceVisible);
            }
            else if (member is FieldDeclaration field)
            {
                scope.Fields[field.Name] = field;
            }
        }

        private void BuildNamespaceScope(NamespaceDeclaration ns, TypeChecker.NamespaceScope parent, string parentPath)
        {
            string nsPath = parentPath.Length > 0 ? $"{parentPath}${ns.Name}" : ns.Name;
            if (!parent.Children.TryGetValue(ns.Name, out TypeChecker.NamespaceScope? scope))
            {
                scope = new TypeChecker.NamespaceScope { Parent = parent };
                parent.Children[ns.Name] = scope;
            }
            _symbols.NamespaceScopes[ns] = scope;

            foreach (AstNode member in ns.Members)
            {
                RegisterMemberInScope(member, scope, nsPath, _namespaceVisible?.Invoke(member) ?? true);
            }
        }

        private static readonly TypeExpression Int = new NamedTypeExpression("int", null, 0);

        private static bool IsInteger(TypeExpression type)
        {
            if (type is NamedTypeExpression named)
            {
                return named.Name is "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "extralong" or "char";
            }
            return false;
        }

        private void RegisterEnum(EnumDeclaration enumDecl, TypeChecker.NamespaceScope scope, string nsPath, bool namespaceVisible = true)
        {
            var previous = _symbols.CurrentNamespace;
            _symbols.CurrentNamespace = scope;
            try { RegisterEnumCore(enumDecl, scope, nsPath, namespaceVisible); }
            finally { _symbols.CurrentNamespace = previous; }
        }

        private void RegisterEnumCore(EnumDeclaration enumDecl, TypeChecker.NamespaceScope scope, string nsPath, bool namespaceVisible)
        {
            TypeExpression underlying = enumDecl.UnderlyingType != null
                ? _symbols.ResolveAlias(enumDecl.UnderlyingType)
                : Int;

            if (!IsInteger(underlying))
            {
                throw new TypeCheckException(
                    $"Enum underlying type must be an integral type, but got '{TypeChecker.TypeNamePublic(underlying)}'",
                    enumDecl.Line);
            }

            TypeChecker.EnumInfo info = new TypeChecker.EnumInfo
            {
                Name = enumDecl.Name,
                Namespace = nsPath,
                UnderlyingType = underlying,
                Accessibility = enumDecl.Accessibility
            };

            EnumConstantEvaluator.Populate(enumDecl, info, _symbols.ResolveEnum, _displayName);

            if (namespaceVisible) scope.Enums[enumDecl.SourceName] = info;
            if (namespaceVisible) scope.TypeNames[enumDecl.SourceName] = enumDecl.Name;
            _symbols.Enums[enumDecl.Name] = info;
            if (nsPath.Length > 0)
            {
                _symbols.Enums[$"{nsPath}::{enumDecl.SourceName}"] = info;
            }
        }

    }
}
