using gflat.ast;
using gflat.CompileExceptions;

namespace gflat
{
    public class TypeChecker : IVisitor
    {
        private readonly Dictionary<AstNode, TypeExpression> _types = new();
        private readonly Stack<Dictionary<string, TypeExpression>> _scopes = new();
        private readonly Dictionary<string, MethodDeclaration> _functions = new();
        private readonly Dictionary<string, ExternDeclaration> _externs = new();

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
                return TypesMatch(pa.Inner, pb.Inner);
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
            // first pass: register all functions
            // unlike SOME languages...
            foreach (NamespaceDeclaration ns in node.Namespaces)
                foreach (AstNode member in ns.Members)
                    if (member is ClassDeclaration cls)
                        foreach (AstNode m in cls.Members)
                        {
                            if (m is MethodDeclaration method)
                                _functions[method.Name] = method;
                            if (m is ExternDeclaration ext)
                                _externs[ext.Name] = ext;
                        }

            // second pass: type check bodies
            foreach (NamespaceDeclaration ns in node.Namespaces)
                ns.Accept(this);
        }

        public void Visit(UsingDirective node) { }

        public void Visit(NamespaceDeclaration node)
        {
            foreach (AstNode member in node.Members)
                member.Accept(this);
        }

        public void Visit(ClassDeclaration node)
        {
            foreach (AstNode member in node.Members)
                member.Accept(this);
        }

        public void Visit(StructDeclaration node) => throw new NotImplementedException();
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
                default:
                    throw new NotImplementedException($"Unary operator {node.Operator} not yet supported");
            }
        }

        public void Visit(LiteralExpression node)
        {
            TypeExpression type = node.Token.Kind switch
            {
                TokenKind.IntLiteral => Int,
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
            node.Value.Accept(this);
            TypeExpression valueType = GetType(node.Value);

            if (node.Target is IdentifierExpression ident)
            {
                TypeExpression targetType = LookupVariable(ident.Name, node.Line);
                if (!TypesMatch(targetType, valueType))
                    throw new TypeCheckException(
                        $"Cannot assign '{TypeName(valueType)}' to '{TypeName(targetType)}'", node.Line);
                RecordType(node, targetType);
            }
            else
            {
                throw new NotImplementedException("Complex assignment targets not yet supported");
            }
        }

        public void Visit(CallExpression node)
        {
            foreach (AstNode arg in node.Arguments)
                arg.Accept(this);

            string funcName = node.Callee is IdentifierExpression ident ? ident.Name
                : node.Callee is MemberAccessExpression mem ? mem.Member
                : throw new NotImplementedException("Complex callee not supported");

            if (_externs.TryGetValue(funcName, out ExternDeclaration? ext))
            {
                RecordType(node, ext.ReturnType);
                return;
            }

            if (!_functions.TryGetValue(funcName, out MethodDeclaration? method))
                throw new TypeCheckException($"Unknown function '{funcName}'", node.Line);

            // check argument count
            if (node.Arguments.Count != method.Parameters.Count)
                throw new TypeCheckException(
                    $"Function '{funcName}' expects {method.Parameters.Count} arguments but got {node.Arguments.Count}",
                    node.Line);

            // check argument types
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
        public void Visit(MemberAccessExpression node) => throw new NotImplementedException();
        public void Visit(InterpolatedStringExpression node) => throw new NotImplementedException();
        public void Visit(NewExpression node) => throw new NotImplementedException();
        public void Visit(NamespaceAccessExpression node) => throw new NotImplementedException();
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
    }
}
