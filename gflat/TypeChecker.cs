using gflat.ast;
using gflat.CompileExceptions;

namespace gflat
{
    public class TypeChecker : IVisitor
    {
        private readonly Dictionary<AstNode, TypeExpression> _types = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _scopes = new();

        private readonly NamespaceScope _globalScope = new();
        private NamespaceScope _currentNamespace = null!;
        private readonly Dictionary<NamespaceDeclaration, NamespaceScope> _namespaceScopes = new();

        private Dictionary<string, StructInfo> _structs = new();
        private readonly Dictionary<MethodDeclaration, string> _functionNamespaces = new();
        private readonly Dictionary<CallExpression, AstNode> _resolvedCalls = new();

        public string GetFunctionNamespace(MethodDeclaration method) =>
            _functionNamespaces.TryGetValue(method, out string? ns) ? ns : "";

        public AstNode? GetResolvedCall(CallExpression call) =>
            _resolvedCalls.TryGetValue(call, out AstNode? target) ? target : null;

        public StructInfo? GetStruct(string name) =>
            _structs.TryGetValue(name, out StructInfo? info) ? info : null;

        private StructInfo? _currentStruct = null;

        public class StructInfo
        {
            public string Name = "";
            public string Namespace = "";
            public List<(string Name, TypeExpression Type)> Fields = new();
            public Dictionary<string, MethodDeclaration> Methods = new();
            public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);
        }

        private class NamespaceScope
        {
            public Dictionary<string, MethodDeclaration> Functions = new();
            public Dictionary<string, ExternDeclaration> Externs = new();
            public Dictionary<string, NamespaceScope> Children = new();
            public NamespaceScope? Parent;
        }

        public TypeExpression GetType(AstNode node)
        {
            if (_types.TryGetValue(node, out TypeExpression? type))
                return type;
            throw new Exception($"No type recorded for node {node.GetType().Name}");
        }

        private void RecordType(AstNode node, TypeExpression type) => _types[node] = type;

        private void PushScope() => _scopes.Push(new Dictionary<string, TypeExpression>());
        private void PopScope() => _scopes.Pop();

        private void DeclareVariable(string name, TypeExpression type, int line)
        {
            if (_scopes.Peek().ContainsKey(name))
                throw new TypeCheckException($"Variable '{name}' already declared in this scope", line);
            _scopes.Peek()[name] = type;
        }

        private TypeExpression LookupVariable(string name, int line)
        {
            foreach (Dictionary<string, TypeExpression> scope in _scopes)
                if (scope.TryGetValue(name, out TypeExpression? type))
                    return type;

            if (_currentStruct != null)
            {
                int idx = _currentStruct.FieldIndex(name);
                if (idx >= 0)
                    return _currentStruct.Fields[idx].Type;
            }

            throw new TypeCheckException($"Unknown variable '{name}'", line);
        }

        private static NamedTypeExpression Int => new NamedTypeExpression("int", null, 0);
        private static NamedTypeExpression Long => new NamedTypeExpression("long", null, 0);
        private static NamedTypeExpression Float => new NamedTypeExpression("float", null, 0);
        private static NamedTypeExpression Bool => new NamedTypeExpression("bool", null, 0);
        private static NamedTypeExpression Char => new NamedTypeExpression("char", null, 0);
        private static PointerTypeExpression CharPtr => new PointerTypeExpression(Char, false, 0);

        // leaving this commented till std::String is a thing
        //private static NamedTypeExpression String => new NamedTypeExpression("string", null, 0);
        private static NamedTypeExpression Void => new NamedTypeExpression("void", null, 0);
        private static NamedTypeExpression Null => new NamedTypeExpression("null", null, 0);

        private static bool IsNumeric(TypeExpression type) =>
            type is NamedTypeExpression n && n.Name is "int" or "long" or "float" or "extralong";

        private static bool IsNullable(TypeExpression type) =>
            type is PointerTypeExpression { IsNullable: true } or
            ManagedTypeExpression { IsNullable: true };

        private static bool TypesMatch(TypeExpression a, TypeExpression b)
        {
            // null is assignable to any nullable type
            if (b is NamedTypeExpression { Name: "null" } && IsNullable(a)) return true;
            if (a is NamedTypeExpression { Name: "null" } && IsNullable(b)) return true;

            if (a is NamedTypeExpression na && b is NamedTypeExpression nb)
                return na.Name == nb.Name;
            if (a is PointerTypeExpression pa && b is PointerTypeExpression pb)
            {
                if (pa.Inner is NamedTypeExpression { Name: "void" } || pb.Inner is NamedTypeExpression { Name: "void" })
                    return true;
                return TypesMatch(pa.Inner, pb.Inner);
            }
            if (a is ArrayTypeExpression aa && b is ArrayTypeExpression ab)
                return TypesMatch(aa.ElementType, ab.ElementType);

            return false;
        }

        public static void Check(CompilationUnit root)
        {
            TypeChecker checker = new TypeChecker();
            root.Accept(checker);
        }

        public void Visit(CompilationUnit node)
        {
            // first pass: register top-level members in global scope
            foreach (AstNode member in node.Members)
                RegisterMemberInScope(member, _globalScope, "");

            // and nested namespaces
            foreach (NamespaceDeclaration ns in node.Namespaces)
                BuildNamespaceScope(ns, _globalScope, "");

            // second pass: type check bodies
            _currentNamespace = _globalScope;
            foreach (AstNode member in node.Members)
                member.Accept(this);

            foreach (NamespaceDeclaration ns in node.Namespaces)
                ns.Accept(this);
        }

        private void RegisterMemberInScope(AstNode member, NamespaceScope scope, string nsPath)
        {
            if (member is MethodDeclaration method)
            {
                scope.Functions[method.Name] = method;
                _functionNamespaces[method] = nsPath;
            }
            else if (member is ExternDeclaration ext)
            {
                scope.Externs[ext.Name] = ext;
            }
            else if (member is StructDeclaration str)
            {
                StructInfo info = new StructInfo
                {
                    Name = str.Name,
                    Namespace = nsPath
                };
                foreach (AstNode m in str.Members)
                {
                    if (m is FieldDeclaration field)
                        info.Fields.Add((field.Name, field.Type));
                    else if (m is MethodDeclaration sm)
                    {
                        info.Methods[sm.Name] = sm;
                        _functionNamespaces[sm] = nsPath;
                    }
                }
                _structs[str.Name] = info;
            }
            else if (member is ClassDeclaration cls)
            {
                foreach (AstNode m in cls.Members)
                {
                    if (m is MethodDeclaration cm)
                    {
                        scope.Functions[cm.Name] = cm;
                        _functionNamespaces[cm] = nsPath;
                    }
                    if (m is ExternDeclaration ce)
                        scope.Externs[ce.Name] = ce;
                }
            }
            else if (member is NamespaceDeclaration nested)
            {
                BuildNamespaceScope(nested, scope, nsPath.Length > 0 ? $"{nsPath}${nested.Name}" : nested.Name);
            }
        }

        private void BuildNamespaceScope(NamespaceDeclaration ns, NamespaceScope parent, string parentPath)
        {
            string nsPath = parentPath.Length > 0 ? $"{parentPath}${ns.Name}" : ns.Name;
            NamespaceScope scope = new NamespaceScope { Parent = parent };
            parent.Children[ns.Name] = scope;
            _namespaceScopes[ns] = scope;

            foreach (AstNode member in ns.Members)
                RegisterMemberInScope(member, scope, nsPath);
        }

        public void Visit(UsingDirective node) { }

        public void Visit(NamespaceDeclaration node)
        {
            NamespaceScope previous = _currentNamespace;
            _currentNamespace = _namespaceScopes[node];

            foreach (AstNode member in node.Members)
                member.Accept(this);

            _currentNamespace = previous;
        }

        public void Visit(ClassDeclaration node)
        {
            foreach (AstNode member in node.Members)
                member.Accept(this);
        }

        public void Visit(StructDeclaration node)
        {
            StructInfo? previousStruct = _currentStruct;
            _currentStruct = _structs[node.Name];

            foreach (AstNode member in node.Members)
            {
                if (member is MethodDeclaration method)
                {
                    PushScope();
                    var structType = new NamedTypeExpression(node.Name, null, method.Line);
                    var thisType = new PointerTypeExpression(structType, false, method.Line);
                    DeclareVariable("this", thisType, method.Line);

                    foreach (Parameter p in method.Parameters)
                        DeclareVariable(p.Name, p.Type, p.Line);

                    method.Body.Accept(this);
                    PopScope();
                }
            }

            _currentStruct = previousStruct;
        }

        public void Visit(InterfaceDeclaration node) => throw new NotImplementedException();
        public void Visit(FieldDeclaration node) => throw new NotImplementedException();

        public void Visit(MethodDeclaration node)
        {
            PushScope();
            foreach (Parameter p in node.Parameters)
                DeclareVariable(p.Name, p.Type, p.Line);
            node.Body.Accept(this);
            PopScope();
        }

        public void Visit(ConstructorDeclaration node)
        {
            PushScope();
            foreach (Parameter p in node.Parameters)
                DeclareVariable(p.Name, p.Type, p.Line);
            node.Body.Accept(this);
            PopScope();
        }

        public void Visit(Parameter node) { }

        public void Visit(BlockStatement node)
        {
            PushScope();
            foreach (AstNode statement in node.Statements)
                statement.Accept(this);
            PopScope();
        }

        public void Visit(ReturnStatement node)
        {
            if (node.Value != null)
                node.Value.Accept(this);
        }

        public void Visit(IfStatement node)
        {
            node.Condition.Accept(this);
            TypeExpression condType = GetType(node.Condition);
            if (!TypesMatch(condType, Bool))
                throw new TypeCheckException("If condition must be bool", node.Line);
            node.Then.Accept(this);
            node.Else?.Accept(this);
        }

        public void Visit(WhileStatement node)
        {
            node.Condition.Accept(this);
            TypeExpression condType = GetType(node.Condition);
            if (!TypesMatch(condType, Bool))
                throw new TypeCheckException("While condition must be bool", node.Line);
            node.Body.Accept(this);
        }

        public void Visit(ForStatement node)
        {
            PushScope();
            node.Initializer?.Accept(this);
            if (node.Condition != null)
            {
                node.Condition.Accept(this);
                TypeExpression condType = GetType(node.Condition);
                if (!TypesMatch(condType, Bool))
                    throw new TypeCheckException("For condition must be bool", node.Line);
            }
            node.Increment?.Accept(this);
            node.Body.Accept(this);
            PopScope();
        }

        public void Visit(VariableDeclaration node)
        {
            if (node.Initializer != null)
            {
                node.Initializer.Accept(this);
                TypeExpression initType = GetType(node.Initializer);
                if (!TypesMatch(node.Type, initType))
                    throw new TypeCheckException(
                        $"Cannot assign '{TypeName(initType)}' to '{TypeName(node.Type)}'", node.Line);
            }
            DeclareVariable(node.Name, node.Type, node.Line);
        }

        public void Visit(ExpressionStatement node)
        {
            node.Expression.Accept(this);
        }

        public void Visit(BinaryExpression node)
        {
            node.Left.Accept(this);
            node.Right.Accept(this);
            TypeExpression left = GetType(node.Left);
            TypeExpression right = GetType(node.Right);

            bool isComparison = node.Operator is
                TokenKind.EqualsEquals or TokenKind.NotEquals or
                TokenKind.Less or TokenKind.Greater or
                TokenKind.LessEquals or TokenKind.GreaterEquals;

            bool isLogical = node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe;

            if (isLogical)
            {
                if (!TypesMatch(left, Bool) || !TypesMatch(right, Bool))
                    throw new TypeCheckException("Logical operators require bool operands", node.Line);
                RecordType(node, Bool);
            }
            else if (isComparison)
            {
                if (!TypesMatch(left, right))
                    throw new TypeCheckException(
                        $"Cannot compare '{TypeName(left)}' with '{TypeName(right)}'", node.Line);
                RecordType(node, Bool);
            }
            else
            {
                if (!TypesMatch(left, right))
                    throw new TypeCheckException(
                        $"Cannot apply operator to '{TypeName(left)}' and '{TypeName(right)}'", node.Line);
                RecordType(node, left);
            }
        }

        public void Visit(UnaryExpression node)
        {
            node.Operand.Accept(this);
            TypeExpression operand = GetType(node.Operand);

            switch (node.Operator)
            {
                case TokenKind.Bang:
                    if (!TypesMatch(operand, Bool))
                        throw new TypeCheckException("'!' requires bool operand", node.Line);
                    RecordType(node, Bool);
                    break;
                case TokenKind.Minus:
                    if (!IsNumeric(operand))
                        throw new TypeCheckException("'-' requires numeric operand", node.Line);
                    RecordType(node, operand);
                    break;
                case TokenKind.PlusPlus:
                case TokenKind.MinusMinus:
                    if (!IsNumeric(operand))
                        throw new TypeCheckException("'++/--' requires numeric operand", node.Line);
                    RecordType(node, operand);
                    break;
                case TokenKind.Ampersand:
                    RecordType(node, new PointerTypeExpression(operand, false, node.Line));
                    break;
                case TokenKind.Star:
                    if (operand is not PointerTypeExpression ptr)
                        throw new TypeCheckException("Cannot dereference non-pointer", node.Line);
                    RecordType(node, ptr.Inner);
                    break;
                default:
                    throw new NotImplementedException($"Unary operator {node.Operator} not yet supported");
            }
        }

        public void Visit(LiteralExpression node)
        {
            TypeExpression type = node.Token.Kind switch
            {
                TokenKind.IntLiteral => Int,
                TokenKind.HexInt => Int,
                TokenKind.FloatLiteral => Float,
                TokenKind.DoubleLiteral => Float,
                TokenKind.LongLiteral => Long,
                TokenKind.StringLiteral => CharPtr,
                TokenKind.CharLiteral => Char,
                TokenKind.True => Bool,
                TokenKind.False => Bool,
                TokenKind.Null => Null,
                TokenKind.InterpolatedStringSegment => CharPtr,
                _ => throw new TypeCheckException($"Unknown literal type {node.Token.Kind}", node.Line)
            };
            RecordType(node, type);
        }

        public void Visit(IdentifierExpression node)
        {
            TypeExpression type = LookupVariable(node.Name, node.Line);
            RecordType(node, type);
        }

        public void Visit(AssignmentExpression node)
        {
            if (node.Target is not (IdentifierExpression or MemberAccessExpression or UnaryExpression { Operator: TokenKind.Star }))
                throw new TypeCheckException($"Invalid assignment target '{node.Target.GetType().Name}'", node.Line);

            if (node.Target is MemberAccessExpression { IsArrow: true } arrow)
                throw new TypeCheckException($"Cannot assign to read-only pointer property '->{arrow.Member}'", node.Line);

            node.Target.Accept(this);
            TypeExpression targetType = GetType(node.Target);

            node.Value.Accept(this);
            TypeExpression valueType = GetType(node.Value);

            if (!TypesMatch(targetType, valueType))
                throw new TypeCheckException(
                    $"Cannot assign '{TypeName(valueType)}' to '{TypeName(targetType)}'", node.Line);

            RecordType(node, targetType);
        }

        public void Visit(CallExpression node)
        {
            foreach (AstNode arg in node.Arguments)
                arg.Accept(this);

            string? funcName = null;
            MethodDeclaration? method = null;
            ExternDeclaration? ext = null;

            if (node.Callee is MemberAccessExpression memberAccess)
            {
                if (memberAccess.IsArrow)
                {
                    memberAccess.Object.Accept(this);
                    TypeExpression targetObjType = GetType(memberAccess.Object);
                    if (targetObjType is not PointerTypeExpression && targetObjType is not ManagedTypeExpression)
                        throw new TypeCheckException($"Cannot use '->' operator on non-pointer type '{TypeName(targetObjType)}'", node.Line);

                    if (memberAccess.Member == "free")
                    {
                        if (node.Arguments.Count != 0)
                            throw new TypeCheckException("'free()' takes no arguments", node.Line);

                        RecordType(node, Void);
                        return;
                    }
                    else if (memberAccess.Member is "address" or "is_null")
                    {
                        throw new TypeCheckException($"'->{memberAccess.Member}' is a property, not a method", node.Line);
                    }
                    else
                    {
                        TypeExpression innerType = targetObjType is PointerTypeExpression p ? p.Inner : ((ManagedTypeExpression)targetObjType).Inner;
                        if (innerType is NamedTypeExpression namedStr && _structs.ContainsKey(namedStr.Name))
                        {
                            throw new TypeCheckException($"The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free'). Use '.' to call method '{memberAccess.Member}' on '{namedStr.Name}'.", node.Line);
                        }
                        throw new TypeCheckException($"Unknown pointer operation '->{memberAccess.Member}'. The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free').", node.Line);
                    }
                }

                memberAccess.Object.Accept(this);
                TypeExpression objType = GetType(memberAccess.Object);
                if (objType is PointerTypeExpression ptr)
                    objType = ptr.Inner;

                if (objType is not NamedTypeExpression named || !_structs.TryGetValue(named.Name, out StructInfo? sInfo))
                    throw new TypeCheckException("Cannot call method on non-struct type", node.Line);

                if (!sInfo.Methods.TryGetValue(memberAccess.Member, out method))
                    throw new TypeCheckException($"'{named.Name}' has no method '{memberAccess.Member}'", node.Line);

                funcName = $"{named.Name}.{memberAccess.Member}";
                _resolvedCalls[node] = method;
            }
            else if (node.Callee is IdentifierExpression ident)
            {
                funcName = ident.Name;
                method = ResolveFunction(funcName);
                ext = method == null ? ResolveExtern(funcName) : null;
            }
            else if (node.Callee is NamespaceAccessExpression nsAccess)
            {
                nsAccess.Accept(this);
                RecordType(node, GetType(nsAccess));
                NamespaceScope? scope = ResolveNamespace(nsAccess.Left);
                if (scope != null)
                {
                    if (scope.Functions.TryGetValue(nsAccess.Member, out MethodDeclaration? m))
                        _resolvedCalls[node] = m;
                    else if (scope.Externs.TryGetValue(nsAccess.Member, out ExternDeclaration? e))
                        _resolvedCalls[node] = e;
                }
                return;
            }
            else
            {
                throw new NotImplementedException("Complex callee not supported");
            }

            if (ext != null)
            {
                _resolvedCalls[node] = ext;
                RecordType(node, ext.ReturnType);
                return;
            }

            if (method == null)
                throw new TypeCheckException($"Unknown function '{funcName}'", node.Line);

            _resolvedCalls[node] = method;

            if (node.Arguments.Count != method.Parameters.Count)
                throw new TypeCheckException(
                    $"Function '{funcName}' expects {method.Parameters.Count} arguments but got {node.Arguments.Count}",
                    node.Line);

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                TypeExpression argType = GetType(node.Arguments[i]);
                TypeExpression paramType = method.Parameters[i].Type;
                if (!TypesMatch(argType, paramType))
                    throw new TypeCheckException(
                        $"Argument {i + 1} of '{funcName}': cannot pass '{TypeName(argType)}' as '{TypeName(paramType)}'",
                        node.Line);
            }

            RecordType(node, method.ReturnType);
        }

        private MethodDeclaration? ResolveFunction(string name)
        {
            NamespaceScope? scope = _currentNamespace;
            while (scope != null)
            {
                if (scope.Functions.TryGetValue(name, out MethodDeclaration? method))
                    return method;
                scope = scope.Parent;
            }
            return null;
        }

        private ExternDeclaration? ResolveExtern(string name)
        {
            NamespaceScope? scope = _currentNamespace;
            while (scope != null)
            {
                if (scope.Externs.TryGetValue(name, out ExternDeclaration? ext))
                    return ext;
                scope = scope.Parent;
            }
            return null;
        }

        public void Visit(NamespaceAccessExpression node)
        {
            // resolve the left side to a namespace scope
            NamespaceScope? scope = ResolveNamespace(node.Left);
            if (scope == null)
                throw new TypeCheckException($"Could not resolve namespace", node.Line);

            if (scope.Functions.TryGetValue(node.Member, out MethodDeclaration? method))
            {
                RecordType(node, method.ReturnType);
                return;
            }
            if (scope.Externs.TryGetValue(node.Member, out ExternDeclaration? ext))
            {
                RecordType(node, ext.ReturnType);
                return;
            }
            throw new TypeCheckException($"'{node.Member}' not found in namespace", node.Line);
        }

        private NamespaceScope? ResolveNamespace(AstNode node)
        {
            if (node is GlobalExpression)
                return _globalScope;

            if (node is IdentifierExpression ident)
            {
                // look for child namespace relative to current
                NamespaceScope? scope = _currentNamespace;
                while (scope != null)
                {
                    if (scope.Children.TryGetValue(ident.Name, out NamespaceScope? child))
                        return child;
                    scope = scope.Parent;
                }
                return null;
            }

            if (node is NamespaceAccessExpression access)
            {
                NamespaceScope? parent = ResolveNamespace(access.Left);
                if (parent == null) return null;
                parent.Children.TryGetValue(access.Member, out NamespaceScope? child);
                return child;
            }

            return null;
        }

        public void Visit(MemberAccessExpression node)
        {
            node.Object.Accept(this);
            TypeExpression objType = GetType(node.Object);

            if (node.IsArrow)
            {
                if (objType is not PointerTypeExpression && objType is not ManagedTypeExpression)
                    throw new TypeCheckException($"Cannot use '->' operator on non-pointer type '{TypeName(objType)}'", node.Line);

                if (node.Member == "address")
                {
                    RecordType(node, Long);
                    return;
                }
                else if (node.Member == "is_null")
                {
                    RecordType(node, Bool);
                    return;
                }
                else if (node.Member == "free")
                {
                    RecordType(node, Void);
                    return;
                }
                else
                {
                    TypeExpression innerType = objType is PointerTypeExpression p ? p.Inner : ((ManagedTypeExpression)objType).Inner;
                    if (innerType is NamedTypeExpression namedStr && _structs.ContainsKey(namedStr.Name))
                    {
                        throw new TypeCheckException($"The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free'). Use '.' to access member '{node.Member}' on '{namedStr.Name}'.", node.Line);
                    }
                    throw new TypeCheckException($"Unknown pointer operation '->{node.Member}'. The '->' operator is reserved for pointer metadata and lifecycle operations ('address', 'is_null', 'free').", node.Line);
                }
            }

            // unwrap pointer if needed
            if (objType is PointerTypeExpression ptr)
                objType = ptr.Inner;

            if (objType is not NamedTypeExpression named)
                throw new TypeCheckException("Member access on non-struct type", node.Line);

            if (!_structs.TryGetValue(named.Name, out StructInfo? info))
                throw new TypeCheckException($"'{named.Name}' is not a struct", node.Line);

            int idx = info.FieldIndex(node.Member);
            if (idx >= 0)
            {
                RecordType(node, info.Fields[idx].Type);
                return;
            }

            if (info.Methods.TryGetValue(node.Member, out MethodDeclaration? method))
            {
                RecordType(node, method.ReturnType);
                return;
            }

            throw new TypeCheckException($"'{named.Name}' has no field or method '{node.Member}'", node.Line);
        }

        public void Visit(InterpolatedStringExpression node) => throw new NotImplementedException();
        public void Visit(NewExpression node) => throw new NotImplementedException();
        public void Visit(NamedTypeExpression node) { }
        public void Visit(PointerTypeExpression node) { }
        public void Visit(ManagedTypeExpression node) { }
        public void Visit(ArrayTypeExpression node) { }
        public void Visit(BreakStatement node) { }
        public void Visit(ContinueStatement node) { }

        public void Visit(AttributeNode node) { }
        public void Visit(ExternDeclaration node) { }

        private static string TypeName(TypeExpression type) => type switch
        {
            NamedTypeExpression n => n.Name,
            PointerTypeExpression p => TypeName(p.Inner) + "*",
            ManagedTypeExpression m => TypeName(m.Inner) + "^",
            ArrayTypeExpression a => TypeName(a.ElementType) + "[]",
            _ => "unknown"
        };

        public void Visit(GlobalExpression node) { }
    }
}
