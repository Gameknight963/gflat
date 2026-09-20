using System;
using System.Collections.Generic;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.symbols;

namespace gflat.semantics
{
    public class SymbolCollectionPass
    {
        private readonly SymbolTable _symbols;

        public SymbolCollectionPass(SymbolTable symbols)
        {
            _symbols = symbols;
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
                RegisterMemberInScope(member, _symbols.GlobalScope, "");
            }

            foreach (NamespaceDeclaration ns in node.Namespaces)
            {
                BuildNamespaceScope(ns, _symbols.GlobalScope, "");
            }
        }

        private void RegisterMemberInScope(AstNode member, TypeChecker.NamespaceScope scope, string nsPath)
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
                scope.TypeNames[iface.SourceName] = iface.Name;
                TypeChecker.InterfaceInfo info = new TypeChecker.InterfaceInfo
                {
                    Name = iface.Name,
                    Namespace = nsPath,
                    Accessibility = iface.Accessibility
                };
                int slotIndex = 0;
                foreach (AstNode m in iface.Members)
                {
                    if (m is MethodDeclaration sm)
                    {
                        if (info.MethodsByName.ContainsKey(sm.Name))
                        {
                            throw new TypeCheckException($"Interface '{iface.Name}' already contains a method named '{sm.Name}'", sm.Line);
                        }
                        info.Methods.Add(sm);
                        info.MethodsByName[sm.Name] = sm;
                        info.MethodIndices[sm.Name] = slotIndex++;
                    }
                    else
                    {
                        throw new TypeCheckException($"Interface '{iface.Name}' can only contain method declarations", m.Line);
                    }
                }
                _symbols.Interfaces[iface.Name] = info;
                scope.Interfaces[iface.SourceName] = info;
            }
            else if (member is StructDeclaration str)
            {
                scope.TypeNames[str.SourceName] = str.Name;
                if (str.IsGeneric)
                {
                    _symbols.GenericStructs[str.Name] = str;
                    return;
                }
                TypeChecker.StructInfo info = new TypeChecker.StructInfo
                {
                    Name = str.Name,
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
                        qualifiedCls.Span = nestedCls.Span;
                        str.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath);
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
                        str.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath);
                    }
                    else if (m is FieldDeclaration field)
                    {
                        info.Fields.Add((field.Name, field.Type));
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
                            throw new TypeCheckException($"Struct '{str.Name}' already defines a destructor", dtor.Line);
                        }
                        if (dtor.IsVirtual)
                        {
                            throw new TypeCheckException($"Struct '{str.Name}' destructor cannot be virtual", dtor.Line);
                        }
                        string shortStrName = str.Name.Split('.').Last().Split('$').Last();
                        if (dtor.Name != str.Name && dtor.Name != shortStrName)
                        {
                            throw new TypeCheckException($"Destructor name '~{dtor.Name}' does not match struct name '{str.Name}'", dtor.Line);
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
                        RegisterEnum(enumDecl, scope, nsPath.Length > 0 ? $"{nsPath}::{str.Name}" : str.Name);
                    }
                }
                _symbols.Structs[str.Name] = info;
                scope.Structs[str.SourceName] = info;
            }
            else if (member is ClassDeclaration cls)
            {
                scope.TypeNames[cls.SourceName] = cls.Name;
                if (cls.IsGeneric)
                {
                    _symbols.GenericClasses[cls.Name] = cls;
                    return;
                }
                TypeChecker.ClassInfo info = new TypeChecker.ClassInfo
                {
                    Name = cls.Name,
                    Namespace = nsPath,
                    BaseClass = cls.BaseClass,
                    IsAbstract = cls.IsAbstract,
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
                        qualifiedCls.Span = nestedCls.Span;
                        cls.Members[i] = qualifiedCls;
                        RegisterMemberInScope(qualifiedCls, scope, nsPath);
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
                        cls.Members[i] = qualifiedStruct;
                        RegisterMemberInScope(qualifiedStruct, scope, nsPath);
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
                            throw new TypeCheckException($"Class '{cls.Name}' already defines a destructor", dtor.Line);
                        }
                        string shortClsName = cls.Name.Contains('.') ? cls.Name.Substring(cls.Name.LastIndexOf('.') + 1) : cls.Name;
                        if (dtor.Name != cls.Name && dtor.Name != shortClsName)
                        {
                            throw new TypeCheckException($"Destructor name '~{dtor.Name}' does not match class name '{cls.Name}'", dtor.Line);
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
                        if (!cm.IsAbstract && cm.Body == null)
                        {
                            throw new TypeCheckException($"Method '{cm.Name}' must declare a body unless marked abstract", cm.Line);
                        }

                        info.Methods[cm.Name] = (cm, cls.Name);
                        info.AllMethods.Add(cm);
                        _symbols.FunctionNamespaces[cm] = nsPath;
                    }
                }

                _symbols.Classes[cls.Name] = info;
                scope.Classes[cls.SourceName] = info;
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
                RegisterEnum(enumDecl, scope, nsPath);
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
                RegisterMemberInScope(member, scope, nsPath);
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

        private void RegisterEnum(EnumDeclaration enumDecl, TypeChecker.NamespaceScope scope, string nsPath)
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

            long nextValue = 0;
            foreach (EnumMemberDeclaration memberDecl in enumDecl.Members)
            {
                if (info.Members.ContainsKey(memberDecl.Name))
                {
                    throw new TypeCheckException(
                        $"Enum '{enumDecl.Name}' already contains a member named '{memberDecl.Name}'",
                        memberDecl.Line);
                }

                if (memberDecl.Value != null)
                {
                    nextValue = EvaluateConstantInt(memberDecl.Value, info);
                }

                info.Members[memberDecl.Name] = nextValue;
                nextValue++;
            }

            scope.Enums[enumDecl.SourceName] = info;
            scope.TypeNames[enumDecl.SourceName] = enumDecl.Name;
            _symbols.Enums[enumDecl.Name] = info;
            if (nsPath.Length > 0)
            {
                _symbols.Enums[$"{nsPath}::{enumDecl.SourceName}"] = info;
            }
        }

        private long EvaluateConstantInt(AstNode expr, TypeChecker.EnumInfo enumInfo)
        {
            if (expr is LiteralExpression lit)
            {
                if (lit.Token.Kind == TokenKind.IntLiteral)
                {
                    return long.Parse(lit.Token.Text);
                }
                if (lit.Token.Kind == TokenKind.HexInt)
                {
                    string text = lit.Token.Text;
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        text = text[2..];
                    }
                    return Convert.ToInt64(text, 16);
                }
                if (lit.Token.Kind == TokenKind.LongLiteral)
                {
                    string text = lit.Token.Text.TrimEnd('L', 'l');
                    return long.Parse(text);
                }
                if (lit.Token.Kind == TokenKind.CharLiteral)
                {
                    string text = lit.Token.Text;
                    if (text.Length >= 3 && text[0] == '\'' && text[^1] == '\'')
                    {
                        string inner = text[1..^1];
                        if (inner.StartsWith("\\"))
                        {
                            return inner switch
                            {
                                "\\0" => '\0',
                                "\\n" => '\n',
                                "\\r" => '\r',
                                "\\t" => '\t',
                                "\\\\" => '\\',
                                "\\'" => '\'',
                                _ => inner.Length > 1 ? inner[1] : 0
                            };
                        }
                        return inner.Length > 0 ? inner[0] : 0;
                    }
                }
            }
            else if (expr is UnaryExpression u)
            {
                if (u.Operator == TokenKind.Minus)
                {
                    return -EvaluateConstantInt(u.Operand, enumInfo);
                }
                if (u.Operator == TokenKind.Plus)
                {
                    return EvaluateConstantInt(u.Operand, enumInfo);
                }
                if (u.Operator == TokenKind.Bang)
                {
                    return ~EvaluateConstantInt(u.Operand, enumInfo);
                }
            }
            else if (expr is BinaryExpression bin)
            {
                long left = EvaluateConstantInt(bin.Left, enumInfo);
                long right = EvaluateConstantInt(bin.Right, enumInfo);
                return bin.Operator switch
                {
                    TokenKind.Plus => left + right,
                    TokenKind.Minus => left - right,
                    TokenKind.Star => left * right,
                    TokenKind.Slash => right != 0 ? left / right : throw new TypeCheckException("Division by zero in enum member value", expr.Line),
                    TokenKind.Percent => right != 0 ? left % right : throw new TypeCheckException("Division by zero in enum member value", expr.Line),
                    TokenKind.Pipe => left | right,
                    TokenKind.Ampersand => left & right,
                    TokenKind.Caret => left ^ right,
                    TokenKind.Less => left < right ? 1 : 0,
                    TokenKind.Greater => left > right ? 1 : 0,
                    TokenKind.LessEquals => left <= right ? 1 : 0,
                    TokenKind.GreaterEquals => left >= right ? 1 : 0,
                    TokenKind.EqualsEquals => left == right ? 1 : 0,
                    TokenKind.NotEquals => left != right ? 1 : 0,
                    _ => throw new TypeCheckException($"Operator '{bin.Operator}' not supported in constant expression", expr.Line)
                };
            }
            else if (expr is IdentifierExpression ident)
            {
                if (enumInfo.Members.TryGetValue(ident.Name, out long memberVal))
                {
                    return memberVal;
                }
                TypeChecker.EnumInfo? otherEnum = _symbols.ResolveEnum(ident);
                if (otherEnum != null && otherEnum.Members.TryGetValue(ident.Name, out long otherVal))
                {
                    return otherVal;
                }
                throw new TypeCheckException($"Enum member or constant '{ident.Name}' not found", expr.Line);
            }
            else if (expr is NamespaceAccessExpression nsAccess)
            {
                TypeChecker.EnumInfo? otherEnum = _symbols.ResolveEnum(nsAccess.Left);
                if (otherEnum != null && otherEnum.Members.TryGetValue(nsAccess.Member, out long memberVal))
                {
                    return memberVal;
                }
                throw new TypeCheckException($"Enum member '{nsAccess.Member}' not found", expr.Line);
            }
            else if (expr is MemberAccessExpression memAccess)
            {
                TypeChecker.EnumInfo? otherEnum = _symbols.ResolveEnum(memAccess.Object);
                if (otherEnum != null && otherEnum.Members.TryGetValue(memAccess.Member, out long memberVal))
                {
                    return memberVal;
                }
                throw new TypeCheckException($"Enum member '{memAccess.Member}' not found", expr.Line);
            }

            throw new TypeCheckException("Enum member value must be a constant integer expression", expr.Line);
        }
    }
}
