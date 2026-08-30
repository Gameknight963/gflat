using gflat.ast;

namespace gflat
{
    public class Parser
    {
        private List<Token> _tokens = new();
        private int _pos;

        private Token Current => _tokens[_pos];
        private Token Peek(int offset = 1) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private Token Consume()
        {
            Token token = Current;
            _pos++;
            return token;
        }

        private Token Expect(TokenKind kind)
        {
            if (Current.Kind != kind)
                throw new Exception($"Expected {kind} but got {Current.Kind} '{Current.Text}' on line {Current.Line}");
            return Consume();
        }

        private bool Check(TokenKind kind) => Current.Kind == kind;

        private bool Match(TokenKind kind)
        {
            if (Check(kind)) { Consume(); return true; }
            return false;
        }

        public static CompilationUnit Parse(List<Token> tokens)
        {
            Parser parser = new Parser();
            parser._tokens = tokens;
            parser._pos = 0;
            return parser.ParseCompilationUnit();
        }

        private CompilationUnit ParseCompilationUnit()
        {
            int line = Current.Line;
            List<UsingDirective> usings = new();
            List<NamespaceDeclaration> namespaces = new();

            while (Check(TokenKind.Using))
                usings.Add(ParseUsingDirective());

            while (!Check(TokenKind.EndOfFile))
                namespaces.Add(ParseNamespaceDeclaration());

            return new CompilationUnit(usings, namespaces, line);
        }

        private UsingDirective ParseUsingDirective()
        {
            int line = Current.Line;
            Expect(TokenKind.Using);
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.Semicolon);
            return new UsingDirective(name, line);
        }

        private NamespaceDeclaration ParseNamespaceDeclaration()
        {
            int line = Current.Line;
            Expect(TokenKind.Namespace);
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.OpenBrace);

            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                members.Add(ParseTypeDeclaration());

            Expect(TokenKind.CloseBrace);
            return new NamespaceDeclaration(name, members, line);
        }

        private AstNode ParseTypeDeclaration()
        {
            int line = Current.Line;
            TokenKind accessibility = TokenKind.Private; // default

            // static classes coming soon
            bool isStatic = false;

            while (true)
            {
                if (Check(TokenKind.Public) || Check(TokenKind.Private) ||
                    Check(TokenKind.Protected) || Check(TokenKind.Internal))
                {
                    accessibility = Current.Kind;
                    Consume();
                }
                else if (Check(TokenKind.Static))
                {
                    isStatic = true;
                    Consume();
                    if (isStatic) throw new NotImplementedException("Static classes are not yet supported");
                }
                else break;
            }

            if (Check(TokenKind.Class)) return ParseClassDeclaration(accessibility, line);
            if (Check(TokenKind.Struct)) return ParseStructDeclaration(accessibility, line);
            if (Check(TokenKind.Interface)) return ParseInterfaceDeclaration(accessibility, line);

            throw new Exception($"Expected type declaration on line {Current.Line}");
        }

        private ClassDeclaration ParseClassDeclaration(TokenKind accessibility, int line)
        {
            Expect(TokenKind.Class);
            string name = Expect(TokenKind.Identifier).Text;

            string? baseClass = null;
            List<string> interfaces = new();

            if (Match(TokenKind.Colon))
            {
                // first name after : could be base class or interface
                // we'll treat all as interfaces for now, type checker resolves which is which
                interfaces.Add(Expect(TokenKind.Identifier).Text);
                while (Match(TokenKind.Comma))
                    interfaces.Add(Expect(TokenKind.Identifier).Text);
            }

            Expect(TokenKind.OpenBrace);
            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                members.Add(ParseMember());
            Expect(TokenKind.CloseBrace);

            return new ClassDeclaration(name, baseClass, interfaces, members, accessibility, line);
        }

        private StructDeclaration ParseStructDeclaration(TokenKind accessibility, int line)
        {
            Expect(TokenKind.Struct);
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.OpenBrace);
            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                members.Add(ParseMember());
            Expect(TokenKind.CloseBrace);
            return new StructDeclaration(name, members, accessibility, line);
        }


        private InterfaceDeclaration ParseInterfaceDeclaration(TokenKind accessibility, int line)
        {
            Expect(TokenKind.Interface);
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.OpenBrace);
            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                members.Add(ParseMember());
            Expect(TokenKind.CloseBrace);
            return new InterfaceDeclaration(name, members, accessibility, line);
        }

        private AstNode ParseMember()
        {
            int line = Current.Line;
            List<AttributeNode> attributes = ParseAttributes();
            if (Check(TokenKind.Extern))
                return ParseExternDeclaration(attributes, line);
            TokenKind accessibility = TokenKind.Private;
            bool isStatic = false;
            bool isConst = false;
            bool isReadonly = false;

            while (true)
            {
                if (Check(TokenKind.Public) || Check(TokenKind.Private) ||
                    Check(TokenKind.Protected) || Check(TokenKind.Internal))
                { accessibility = Current.Kind; Consume(); }
                else if (Check(TokenKind.Static)) { isStatic = true; Consume(); }
                else if (Check(TokenKind.Const)) { isConst = true; Consume(); }
                else if (Check(TokenKind.Readonly)) { isReadonly = true; Consume(); }
                else break;
            }

            TypeExpression type = ParseTypeExpression();
            string name;

            if (Check(TokenKind.OpenParen))
            {
                // constructor - type was actually the name
                name = ((NamedTypeExpression)type).Name;
                return ParseConstructorDeclaration(name, accessibility, line);
            }

            name = Expect(TokenKind.Identifier).Text;

            // if followed by ( it's a method, otherwise a field
            if (Check(TokenKind.OpenParen))
                return ParseMethodDeclaration(type, name, accessibility, isStatic, line);

            return ParseFieldDeclaration(type, name, accessibility, isStatic, isConst, isReadonly, line);
        }

        private List<AttributeNode> ParseAttributes()
        {
            List<AttributeNode> attributes = new();
            while (Check(TokenKind.OpenBracket))
            {
                int line = Current.Line;
                Consume(); // [
                string name = Expect(TokenKind.Identifier).Text;
                List<string> args = new();
                if (Match(TokenKind.OpenParen))
                {
                    while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                    {
                        args.Add(Expect(TokenKind.StringLiteral).Text);
                        if (!Check(TokenKind.CloseParen))
                            Expect(TokenKind.Comma);
                    }
                    Expect(TokenKind.CloseParen);
                }
                Expect(TokenKind.CloseBracket);
                attributes.Add(new AttributeNode(name, args, line));
            }
            return attributes;
        }

        private ExternDeclaration ParseExternDeclaration(List<AttributeNode> attributes, int line)
        {
            Expect(TokenKind.Extern);
            TypeExpression returnType = ParseTypeExpression();
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.OpenParen);

            List<Parameter> parameters = new();
            bool isVariadic = false;

            while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
            {
                // check for variadic ...
                if (Check(TokenKind.Dot) && Peek().Kind == TokenKind.Dot && Peek(2).Kind == TokenKind.Dot)
                {
                    Consume(); Consume(); Consume();
                    isVariadic = true;
                    break;
                }
                TypeExpression paramType = ParseTypeExpression();
                string paramName = Expect(TokenKind.Identifier).Text;
                parameters.Add(new Parameter(paramName, paramType, Current.Line));
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }

            Expect(TokenKind.CloseParen);
            Expect(TokenKind.Semicolon);
            return new ExternDeclaration(name, returnType, parameters, isVariadic, attributes, line);
        }

        private ConstructorDeclaration ParseConstructorDeclaration(string name, TokenKind accessibility, int line)
        {
            Expect(TokenKind.OpenParen);
            List<Parameter> parameters = new();
            while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
            {
                TypeExpression paramType = ParseTypeExpression();
                string paramName = Expect(TokenKind.Identifier).Text;
                parameters.Add(new Parameter(paramName, paramType, Current.Line));
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }
            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new ConstructorDeclaration(name, parameters, body, accessibility, line);
        }

        private BlockStatement ParseBodyOrBlock()
        {
            int line = Current.Line;
            if (Check(TokenKind.OpenBrace))
                return ParseBlockStatement();

            AstNode statement = ParseStatement();
            return new BlockStatement(new List<AstNode> { statement }, line);
        }

        private MethodDeclaration ParseMethodDeclaration(TypeExpression returnType, string name, TokenKind accessibility, bool isStatic, int line)
        {
            Expect(TokenKind.OpenParen);
            List<Parameter> parameters = new();
            while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
            {
                TypeExpression paramType = ParseTypeExpression();
                string paramName = Expect(TokenKind.Identifier).Text;
                parameters.Add(new Parameter(paramName, paramType, Current.Line));
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }
            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new MethodDeclaration(name, returnType, parameters, body, accessibility, isStatic, line);
        }

        private FieldDeclaration ParseFieldDeclaration(TypeExpression type, string name, TokenKind accessibility, bool isStatic, bool isConst, bool isReadonly, int line)
        {
            AstNode? initializer = null;
            if (Match(TokenKind.Equals))
                initializer = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new FieldDeclaration(name, type, initializer, accessibility, isConst, isStatic, line);
        }

        private TypeExpression ParseTypeExpression()
        {
            int line = Current.Line;
            string name = "";

            // handle built-in type keywords
            if (Current.Kind is TokenKind.Int or TokenKind.Float or TokenKind.Bool or
                TokenKind.Char or TokenKind.Long or TokenKind.ExtraLong or
                TokenKind.String or TokenKind.Void)
            {
                name = Current.Text;
                Consume();
            }
            else
            {
                name = Expect(TokenKind.Identifier).Text;
            }

            // namespace qualifier e.g. Program::Foo
            string? ns = null;
            if (Check(TokenKind.DoubleColon))
            {
                Consume();
                ns = name;
                name = Expect(TokenKind.Identifier).Text;
            }

            TypeExpression type = new NamedTypeExpression(name, ns, line);

            // postfix modifiers
            if (Match(TokenKind.Star))
            {
                bool nullable = Match(TokenKind.QuestionMark);
                type = new PointerTypeExpression(type, nullable, line);
            }
            else if (Match(TokenKind.Caret))
            {
                bool nullable = Match(TokenKind.QuestionMark);
                type = new ManagedTypeExpression(type, nullable, line);
            }

            if (Check(TokenKind.OpenBracket) && Peek().Kind == TokenKind.CloseBracket)
            {
                Consume(); // [
                Consume(); // ]
                type = new ArrayTypeExpression(type, line);
            }

            return type;
        }

        private BlockStatement ParseBlockStatement()
        {
            int line = Current.Line;
            Expect(TokenKind.OpenBrace);
            List<AstNode> statements = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                statements.Add(ParseStatement());
            Expect(TokenKind.CloseBrace);
            return new BlockStatement(statements, line);
        }

        private AstNode ParseStatement()
        {
            int line = Current.Line;

            switch (Current.Kind)
            {
                case TokenKind.Return:
                    return ParseReturnStatement();
                case TokenKind.If:
                    return ParseIfStatement();
                case TokenKind.While:
                    return ParseWhileStatement();
                case TokenKind.For:
                    return ParseForStatement();
                case TokenKind.OpenBrace:
                    return ParseBodyOrBlock();
                case TokenKind.Break:
                    Consume();
                    Expect(TokenKind.Semicolon);
                    return new BreakStatement(line);
                case TokenKind.Continue:
                    Consume();
                    Expect(TokenKind.Semicolon);
                    return new ContinueStatement(line);
            }

            // variable declaration or expression statement
            // if it starts with a type keyword or identifier followed by identifier, it's a declaration
            if (IsTypeStart() && IsVariableDeclaration())
                return ParseVariableDeclaration();

            AstNode expr = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new ExpressionStatement(expr, line);
        }

        private bool IsTypeStart() =>
            Current.Kind is TokenKind.Int or TokenKind.Float or TokenKind.Bool or
            TokenKind.Char or TokenKind.Long or TokenKind.ExtraLong or
            TokenKind.String or TokenKind.Void or TokenKind.Identifier;

        private bool IsVariableDeclaration()
        {
            // look ahead to see if this is "type name" or "type* name" etc
            int saved = _pos;
            try
            {
                ParseTypeExpression();
                return Check(TokenKind.Identifier);
            }
            catch
            {
                return false;
            }
            finally
            {
                _pos = saved;
            }
        }

        private ReturnStatement ParseReturnStatement()
        {
            int line = Current.Line;
            Expect(TokenKind.Return);
            AstNode? value = null;
            if (!Check(TokenKind.Semicolon))
                value = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new ReturnStatement(value, line);
        }

        private IfStatement ParseIfStatement()
        {
            int line = Current.Line;
            Expect(TokenKind.If);
            Expect(TokenKind.OpenParen);
            AstNode condition = ParseExpression();
            Expect(TokenKind.CloseParen);
            BlockStatement then = ParseBodyOrBlock();
            BlockStatement? else_ = null;
            if (Match(TokenKind.Else))
                else_ = ParseBodyOrBlock();
            return new IfStatement(condition, then, else_, line);
        }

        private WhileStatement ParseWhileStatement()
        {
            int line = Current.Line;
            Expect(TokenKind.While);
            Expect(TokenKind.OpenParen);
            AstNode condition = ParseExpression();
            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new WhileStatement(condition, body, line);
        }

        private ForStatement ParseForStatement()
        {
            int line = Current.Line;
            Expect(TokenKind.For);
            Expect(TokenKind.OpenParen);

            AstNode? initializer = null;
            if (!Check(TokenKind.Semicolon))
                initializer = IsVariableDeclaration() ? ParseVariableDeclaration() : ParseExpression();
            else
                Expect(TokenKind.Semicolon);

            AstNode? condition = null;
            if (!Check(TokenKind.Semicolon))
                condition = ParseExpression();
            Expect(TokenKind.Semicolon);

            AstNode? increment = null;
            if (!Check(TokenKind.CloseParen))
                increment = ParseExpression();

            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new ForStatement(initializer, condition, increment, body, line);
        }

        private VariableDeclaration ParseVariableDeclaration()
        {
            int line = Current.Line;
            TypeExpression type = ParseTypeExpression();
            string name = Expect(TokenKind.Identifier).Text;
            AstNode? initializer = null;
            if (Match(TokenKind.Equals))
                initializer = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new VariableDeclaration(name, type, initializer, line);
        }

        private AstNode ParseExpression(int minBindingPower = 0)
        {
            AstNode left = ParsePrefix();

            while (true)
            {
                (int leftPower, int rightPower) = GetInfixBindingPower(Current.Kind);
                if (leftPower <= minBindingPower) break;

                Token op = Consume();

                // handle postfix operators like ++ and --
                if (op.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
                {
                    left = new UnaryExpression(left, op.Kind, false, op.Line);
                    continue;
                }

                // handle member access
                if (op.Kind is TokenKind.Dot or TokenKind.Arrow)
                {
                    string member = Expect(TokenKind.Identifier).Text;
                    left = new MemberAccessExpression(left, member, op.Kind == TokenKind.Arrow, op.Line);
                    continue;
                }

                // handle call
                if (op.Kind == TokenKind.OpenParen)
                {
                    List<AstNode> args = new();
                    while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                    {
                        args.Add(ParseExpression());
                        if (!Check(TokenKind.CloseParen))
                            Expect(TokenKind.Comma);
                    }
                    Expect(TokenKind.CloseParen);
                    left = new CallExpression(left, args, op.Line);
                    continue;
                }

                // handle assignment operators
                if (op.Kind is TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or
                    TokenKind.StarEquals or TokenKind.SlashEquals or TokenKind.PercentEquals)
                {
                    AstNode right = ParseExpression(rightPower - 1); // right associative
                    left = new AssignmentExpression(left, right, op.Kind, op.Line);
                    continue;
                }

                if (op.Kind == TokenKind.DoubleColon)
                {
                    string member = Expect(TokenKind.Identifier).Text;
                    left = new NamespaceAccessExpression(left, member, op.Line);
                    continue;
                }

                AstNode rightExpr = ParseExpression(rightPower);
                left = new BinaryExpression(left, rightExpr, op.Kind, op.Line);
            }

            return left;
        }

        private AstNode ParsePrefix()
        {
            int line = Current.Line;

            // unary prefix operators
            if (Check(TokenKind.Bang) || Check(TokenKind.Minus) || Check(TokenKind.Star) ||
                Check(TokenKind.Ampersand) || Check(TokenKind.PlusPlus) || Check(TokenKind.MinusMinus))
            {
                Token op = Consume();
                AstNode operand = ParseExpression(8); // high binding power for prefix
                return new UnaryExpression(operand, op.Kind, true, line);
            }

            // parenthesized expression
            if (Check(TokenKind.OpenParen))
            {
                Consume();
                AstNode expr = ParseExpression();
                Expect(TokenKind.CloseParen);
                return expr;
            }

            // interpolated string
            if (Check(TokenKind.InterpolatedStringSegment) || Check(TokenKind.InterpolatedStringExprStart))
                return ParseInterpolatedString();

            // literals
            if (Current.Kind is TokenKind.IntLiteral or TokenKind.FloatLiteral or TokenKind.DoubleLiteral or
                TokenKind.LongLiteral or TokenKind.HexInt or TokenKind.StringLiteral or
                TokenKind.CharLiteral or TokenKind.True or TokenKind.False or TokenKind.Null)
            {
                Token token = Consume();
                return new LiteralExpression(token, line);
            }

            // new expression
            if (Check(TokenKind.New))
            {
                Consume();
                TypeExpression type = ParseTypeExpression();
                Expect(TokenKind.OpenParen);
                List<AstNode> args = new();
                while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                {
                    args.Add(ParseExpression());
                    if (!Check(TokenKind.CloseParen))
                        Expect(TokenKind.Comma);
                }
                Expect(TokenKind.CloseParen);
                return new NewExpression(type, args, line);
            }

            // identifier
            if (Check(TokenKind.Identifier))
            {
                Token token = Consume();
                return new IdentifierExpression(token.Text, line);
            }

            throw new Exception($"Unexpected token '{Current.Text}' on line {line}");
        }

        private static (int left, int right) GetInfixBindingPower(TokenKind kind) => kind switch
        {
            TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or
            TokenKind.StarEquals or TokenKind.SlashEquals or TokenKind.PercentEquals => (1, 1),
            TokenKind.PipePipe => (2, 3),
            TokenKind.AmpersandAmpersand => (4, 5),
            TokenKind.EqualsEquals or TokenKind.NotEquals => (6, 7),
            TokenKind.Less or TokenKind.Greater or
            TokenKind.LessEquals or TokenKind.GreaterEquals => (8, 9),
            TokenKind.Plus or TokenKind.Minus => (10, 11),
            TokenKind.Star or TokenKind.Slash or TokenKind.Percent => (12, 13),
            TokenKind.PlusPlus or TokenKind.MinusMinus => (14, 0),
            TokenKind.Dot or TokenKind.Arrow or TokenKind.DoubleColon => (16, 17),
            TokenKind.OpenParen => (16, 0),
            _ => (0, 0)
        };

        private InterpolatedStringExpression ParseInterpolatedString()
        {
            int line = Current.Line;
            List<AstNode> parts = new();

            while (Check(TokenKind.InterpolatedStringSegment) || Check(TokenKind.InterpolatedStringExprStart))
            {
                if (Check(TokenKind.InterpolatedStringSegment))
                {
                    Token segment = Consume();
                    parts.Add(new LiteralExpression(segment, segment.Line));
                }
                else if (Check(TokenKind.InterpolatedStringExprStart))
                {
                    Consume(); // {
                    parts.Add(ParseExpression());
                    Expect(TokenKind.InterpolatedStringExprEnd);
                }
            }

            return new InterpolatedStringExpression(parts, line);
        }
    }
}
