using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat
{
    public class Parser
    {
        private List<Token> _tokens = new();
        private int _pos;
        private readonly DiagnosticBag _diagnostics;
        private bool _isPrelude; // When true, skip allocator prelude injection (used for internal prelude parsing)

        public DiagnosticBag Diagnostics => _diagnostics;

        public Parser(DiagnosticBag? diagnostics = null)
        {
            _diagnostics = diagnostics ?? new DiagnosticBag();
        }

        private T Located<T>(Func<T> parse) where T : AstNode?
        {
            Token start = Current;
            using var context = SourceContext.Enter(start.Span);
            T result = parse();
            if (result != null && start.Source != null)
            {
                int end = _pos > 0 ? _tokens[_pos - 1].End + 1 : start.Start;
                result.Span = start.Source.Span(start.Start, Math.Max(0, end - start.Start));
            }
            return result;
        }

        private Token Current => _tokens[_pos];
        private Token Peek(int offset = 1) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private Token Consume()
        {
            Token token = Current;
            if (_pos < _tokens.Count - 1)
            {
                _pos++;
            }
            return token;
        }

        private Token Expect(TokenKind kind)
        {
            if (kind == TokenKind.Greater && Current.Kind == TokenKind.GreaterGreater)
            {
                Token pair = Current;
                _tokens[_pos] = new Token(TokenKind.Greater, pair.Line, pair.Start, pair.Start, column: pair.Column) { Source = pair.Source };
                _tokens.Insert(_pos + 1, new Token(TokenKind.Greater, pair.Line, pair.Start + 1, pair.End, column: pair.Column + 1) { Source = pair.Source });
                return Consume();
            }
            if (Current.Kind != kind)
            {
                _diagnostics.Report(DiagnosticRules.GF0005_ExpectedToken, Current.Line, Current.Column, null, kind.ToString(), Current.Kind.ToString(), Current.Text);

                if (kind == TokenKind.Semicolon)
                {
                    return new Token(TokenKind.Semicolon, Current.Line, Current.Start, Current.Start, ";");
                }
                if (kind is TokenKind.CloseParen or TokenKind.CloseBrace or TokenKind.CloseBracket)
                {
                    return new Token(kind, Current.Line, Current.Start, Current.Start);
                }
                if (kind == TokenKind.Identifier && Current.Kind is TokenKind.Semicolon or TokenKind.CloseParen or TokenKind.CloseBrace or TokenKind.CloseBracket or TokenKind.Comma or TokenKind.Equals)
                {
                    return new Token(TokenKind.Identifier, Current.Line, Current.Start, Current.Start, "<missing>");
                }
            }
            return Consume();
        }

        private bool Check(TokenKind kind) => Current.Kind == kind ||
            (kind == TokenKind.Greater && Current.Kind == TokenKind.GreaterGreater);


        private bool Match(TokenKind kind)
        {
            if (Check(kind))
            {
                Consume();
                return true;
            }
            return false;
        }

        public static CompilationUnit Parse(List<Token> tokens, DiagnosticBag? diagnostics = null)
        {
            using var sourceContext = SourceContext.Enter(tokens[0].Span);
            Parser parser = new Parser(diagnostics);
            parser._tokens = tokens;
            parser._pos = 0;
            return parser.ParseCompilationUnit();
        }

        private CompilationUnit ParseCompilationUnit() => Located(() => ParseCompilationUnitCoreLocated());

        private CompilationUnit ParseCompilationUnitCoreLocated()
        {
            int line = Current.Line;
            List<UsingDirective> usings = new();
            List<NamespaceDeclaration> namespaces = new();
            List<AstNode> members = new();

            // Check for compiler directives (#no_default_allocator, etc.) before anything else
            while (Check(TokenKind.Hash))
            {
                Consume(); // #
                if (Current.Kind == TokenKind.Identifier && Current.Text == "no_default_allocator")
                {
                    Consume();
                    _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, line, 0, null, "#no_default_allocator is obsolete; use replace on Allocator::Allocate and Allocator::Free");
                }
                else
                {
                    string directiveName = Current.Kind == TokenKind.Identifier ? Current.Text : Current.Kind.ToString();
                    _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, Current.Line, 0, $"Unknown compiler directive '#{directiveName}'");
                    Consume();
                }
            }

            while (Check(TokenKind.Using))
                usings.Add(ParseUsingDirective());

            while (!Check(TokenKind.EndOfFile))
            {
                if (Check(TokenKind.Namespace))
                    namespaces.Add(ParseNamespaceDeclaration());
                else
                    members.Add(ParseTopLevelMember());
            }

            // Only inject preludes for top-level user code (not recursive prelude parsing)
            if (!_isPrelude)
            {
                bool hasException = members.OfType<ClassDeclaration>().Any(c => c.Name == "Exception");
                if (members.OfType<ClassDeclaration>().Any(c => c.Name == "Attribute"))
                    _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, line, 0, null, "Attribute is a reserved prelude class");
                List<Token> preludeTokens = Lexer.Tokenize(Prelude.Source);
                Parser preludeParser = new Parser();
                preludeParser._tokens = preludeTokens;
                preludeParser._pos = 0;
                preludeParser._isPrelude = true;
                CompilationUnit preludeUnit = preludeParser.ParseCompilationUnit();
                members.InsertRange(0, preludeUnit.Members.Where(m => !(hasException && m is ClassDeclaration c && c.Name == "Exception")));

                Parser allocatorParser = new Parser { _tokens = Lexer.Tokenize(Prelude.AllocatorSource), _isPrelude = true };
                CompilationUnit allocatorUnit = allocatorParser.ParseCompilationUnit();
                namespaces.InsertRange(0, allocatorUnit.Namespaces);
                foreach (AstNode member in allocatorUnit.Members)
                    if (member is not ExternDeclaration ext || !members.OfType<ExternDeclaration>().Any(e => e.Name == ext.Name))
                        members.Add(member);
            }

            return new CompilationUnit(usings, namespaces, members, line);
        }

        private UsingDirective ParseUsingDirective() => Located(() => ParseUsingDirectiveCoreLocated());

        private UsingDirective ParseUsingDirectiveCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Using);
            string name = Expect(TokenKind.Identifier).Text;
            while (Match(TokenKind.DoubleColon))
                name += "::" + Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.Semicolon);
            return new UsingDirective(name, line);
        }

        private NamespaceDeclaration ParseNamespaceDeclaration() => Located(() => ParseNamespaceDeclarationCoreLocated());

        private NamespaceDeclaration ParseNamespaceDeclarationCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Namespace);
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.OpenBrace);

            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
            {
                if (Check(TokenKind.Namespace))
                {
                    members.Add(ParseNamespaceDeclaration());
                    continue;
                }
                members.Add(ParseTopLevelMember());
            }

            Expect(TokenKind.CloseBrace);
            return new NamespaceDeclaration(name, members, line);
        }

        private AstNode ParseTopLevelMember() => Located(() => ParseTopLevelMemberCoreLocated());

        private AstNode ParseTopLevelMemberCoreLocated()
        {
            List<AttributeNode> attributes = ParseAttributes();
            AstNode node = ParseTopLevelMemberCore(attributes);
            node.Attributes = attributes;
            return node;
        }

        private AstNode ParseTopLevelMemberCore(List<AttributeNode> attributes) => Located(() => ParseTopLevelMemberCoreCoreLocated(attributes));

        private AstNode ParseTopLevelMemberCoreCoreLocated(List<AttributeNode> attributes)
        {
            if (Check(TokenKind.Extern))
                return ParseExternDeclaration(attributes, Current.Line);
            if (Check(TokenKind.Class) || Check(TokenKind.Struct) || Check(TokenKind.Interface) || Check(TokenKind.Enum) || Check(TokenKind.Abstract))
                return ParseTypeDeclaration(attributes);

            // free function, field, alias, or type declaration with accessibility modifier
            return ParseFreeFunctionOrField(attributes);
        }

        private AstNode ParseFreeFunctionOrField(List<AttributeNode> attributes) => Located(() => ParseFreeFunctionOrFieldCoreLocated(attributes));

        private AstNode ParseFreeFunctionOrFieldCoreLocated(List<AttributeNode> attributes)
        {
            int line = Current.Line;
            TokenKind accessibility = TokenKind.Public; // default for free functions
            bool isReadonly = false;
            bool isConst = false;
            bool isAbstract = false;
            FunctionImplementationKind implementation = FunctionImplementationKind.Ordinary;
            AstNode Finish(AstNode node) => ApplyImplementationKind(node, implementation);

            while (true)
            {
                if (Check(TokenKind.Public) || Check(TokenKind.Private) ||
                    Check(TokenKind.Protected) || Check(TokenKind.Internal))
                {
                    accessibility = Current.Kind;
                    Consume();
                }
                else if (Check(TokenKind.Readonly) && !isReadonly)
                {
                    isReadonly = true;
                    Consume();
                }
                else if (Check(TokenKind.Const))
                {
                    isConst = true;
                    Consume();
                }
                else if (Check(TokenKind.Abstract))
                {
                    isAbstract = true;
                    Consume();
                }
                else if (Check(TokenKind.Weak) || Check(TokenKind.Replace))
                {
                    if (implementation != FunctionImplementationKind.Ordinary)
                        throw new TypeCheckException("Use exactly one of weak or replace", Current.Line);
                    implementation = Consume().Kind == TokenKind.Weak ? FunctionImplementationKind.Weak : FunctionImplementationKind.Replace;
                }
                else break;
            }

            if (Check(TokenKind.Alias))
                return Finish(ParseAliasDeclaration(accessibility, line));
            if (Check(TokenKind.Enum))
                return Finish(ParseEnumDeclaration(accessibility, line));
            if (Check(TokenKind.Class))
                return Finish(ParseClassDeclaration(accessibility, isAbstract, line, attributes));
            if (Check(TokenKind.Struct))
                return Finish(ParseStructDeclaration(accessibility, line, attributes));
            if (Check(TokenKind.Interface))
                return Finish(ParseInterfaceDeclaration(accessibility, line));

            TypeExpression type = ParseTypeExpression();
            string name = Expect(TokenKind.Identifier).Text;
            List<GenericParameter> genericParams = ParseGenericParameters();

            if (Check(TokenKind.OpenParen))
                return Finish(ParseMethodDeclaration(type, name, accessibility, false, false, false, false, line, isReadonly, isConst, attributes, genericParams));

            return Finish(ParseFieldDeclaration(type, name, accessibility, false, isConst, isReadonly, line));
        }

        private static AstNode ApplyImplementationKind(AstNode node, FunctionImplementationKind kind)
        {
            if (kind == FunctionImplementationKind.Ordinary) return node;
            if (node is not MethodDeclaration method || method.StringLiteralPrefix != null)
                throw new TypeCheckException("weak and replace apply only to ordinary functions and methods", node.Line);
            method.ImplementationKind = kind;
            return method;
        }

        private List<GenericParameter> ParseGenericParameters()
        {
            List<GenericParameter> parameters = new();
            if (Match(TokenKind.Less))
            {
                while (!Check(TokenKind.Greater) && !Check(TokenKind.EndOfFile))
                {
                    int line = Current.Line;
                    string paramName = Expect(TokenKind.Identifier).Text;
                    TypeExpression? constraint = null;
                    if (Match(TokenKind.Colon))
                    {
                        constraint = ParseTypeExpression();
                    }
                    parameters.Add(new GenericParameter(paramName, constraint, line));
                    if (!Check(TokenKind.Greater))
                        Expect(TokenKind.Comma);
                }
                Expect(TokenKind.Greater);
            }
            return parameters;
        }

        private bool IsGenericCallAhead()
        {
            if (!Check(TokenKind.Less))
                return false;
            int depth = 0;
            for (int i = _pos; i < _tokens.Count; i++)
            {
                if (_tokens[i].Kind == TokenKind.Less)
                {
                    depth++;
                }
                else if (_tokens[i].Kind is TokenKind.Greater or TokenKind.GreaterGreater)
                {
                    depth -= _tokens[i].Kind == TokenKind.GreaterGreater ? 2 : 1;
                    if (depth == 0)
                    {
                        return i + 1 < _tokens.Count && _tokens[i + 1].Kind == TokenKind.OpenParen;
                    }
                }
                else if (_tokens[i].Kind is TokenKind.Semicolon or TokenKind.OpenBrace or TokenKind.CloseBrace)
                {
                    return false;
                }
            }
            return false;
        }

        private AstNode ParseTypeDeclaration(List<AttributeNode>? attributes = null) => Located(() => ParseTypeDeclarationCoreLocated(attributes));

        private AstNode ParseTypeDeclarationCoreLocated(List<AttributeNode>? attributes = null)
        {
            int line = Current.Line;
            TokenKind accessibility = TokenKind.Internal;
            bool isStatic = false;
            bool isAbstract = false;

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
                else if (Check(TokenKind.Abstract))
                {
                    isAbstract = true;
                    Consume();
                }
                else break;
            }

            if (Check(TokenKind.Class)) return ParseClassDeclaration(accessibility, isAbstract, line, attributes);
            if (Check(TokenKind.Struct)) return ParseStructDeclaration(accessibility, line, attributes);
            if (Check(TokenKind.Interface)) return ParseInterfaceDeclaration(accessibility, line);
            if (Check(TokenKind.Enum)) return ParseEnumDeclaration(accessibility, line);

            _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, Current.Line, Current.Start, null, $"Expected type declaration on line {Current.Line}");
            Consume();
            return new StructDeclaration("<error>", new List<AstNode>(), TokenKind.Private, line);
        }

        private string ParseTypeNameString()
        {
            if (Check(TokenKind.Dot))
            {
                throw new TypeCheckException($"Type names must use '::' for scoping and nested types, not '.' on line {Current.Line}", Current.Line);
            }
            string tname = Expect(TokenKind.Identifier).Text;
            if (Check(TokenKind.Dot))
            {
                throw new TypeCheckException($"Type names must use '::' for scoping and nested types, not '.' on line {Current.Line}", Current.Line);
            }
            while (Match(TokenKind.DoubleColon))
            {
                if (Check(TokenKind.Dot))
                {
                    throw new TypeCheckException($"Type names must use '::' for scoping and nested types, not '.' on line {Current.Line}", Current.Line);
                }
                tname = $"{tname}::{Expect(TokenKind.Identifier).Text}";
                if (Check(TokenKind.Dot))
                {
                    throw new TypeCheckException($"Type names must use '::' for scoping and nested types, not '.' on line {Current.Line}", Current.Line);
                }
            }
            return tname;
        }

        private ClassDeclaration ParseClassDeclaration(TokenKind accessibility, bool isAbstract, int line, List<AttributeNode>? attributes = null) => Located(() => ParseClassDeclarationCoreLocated(accessibility, isAbstract, line, attributes));

        private ClassDeclaration ParseClassDeclarationCoreLocated(TokenKind accessibility, bool isAbstract, int line, List<AttributeNode>? attributes = null)
        {
            Expect(TokenKind.Class);
            string name = Expect(TokenKind.Identifier).Text;
            List<GenericParameter> genericParams = ParseGenericParameters();

            string? baseClass = null;
            List<string> interfaces = new();

            if (Match(TokenKind.Colon))
            {
                // first name after : could be base class or interface
                // we'll treat all as interfaces for now, type checker resolves which is which
                interfaces.Add(ParseTypeNameString());
                while (Match(TokenKind.Comma))
                    interfaces.Add(ParseTypeNameString());
            }

            Expect(TokenKind.OpenBrace);
            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                members.Add(ParseMember());
            Expect(TokenKind.CloseBrace);

            return new ClassDeclaration(name, baseClass, interfaces, members, accessibility, isAbstract, line, attributes, genericParams);
        }

        private StructDeclaration ParseStructDeclaration(TokenKind accessibility, int line, List<AttributeNode>? attributes = null) => Located(() => ParseStructDeclarationCoreLocated(accessibility, line, attributes));

        private StructDeclaration ParseStructDeclarationCoreLocated(TokenKind accessibility, int line, List<AttributeNode>? attributes = null)
        {
            Expect(TokenKind.Struct);
            string name = Expect(TokenKind.Identifier).Text;
            List<GenericParameter> genericParams = ParseGenericParameters();

            List<string> interfaces = new();
            if (Match(TokenKind.Colon))
            {
                interfaces.Add(ParseTypeNameString());
                while (Match(TokenKind.Comma))
                    interfaces.Add(ParseTypeNameString());
            }

            Expect(TokenKind.OpenBrace);
            List<AstNode> members = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                members.Add(ParseMember());
            Expect(TokenKind.CloseBrace);
            return new StructDeclaration(name, interfaces, members, accessibility, line, attributes, genericParams);
        }


        private InterfaceDeclaration ParseInterfaceDeclaration(TokenKind accessibility, int line) => Located(() => ParseInterfaceDeclarationCoreLocated(accessibility, line));

        private InterfaceDeclaration ParseInterfaceDeclarationCoreLocated(TokenKind accessibility, int line)
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

        private AstNode ParseMember() => Located(() => ParseMemberCoreLocated());

        private AstNode ParseMemberCoreLocated()
        {
            int line = Current.Line;
            List<AttributeNode> attributes = ParseAttributes();
            AstNode node = ParseMemberCore(attributes, line);
            node.Attributes = attributes;
            return node;
        }

        private AstNode ParseMemberCore(List<AttributeNode> attributes, int line) => Located(() => ParseMemberCoreCoreLocated(attributes, line));

        private AstNode ParseMemberCoreCoreLocated(List<AttributeNode> attributes, int line)
        {
            if (Check(TokenKind.Extern))
                return ParseExternDeclaration(attributes, line);
            TokenKind accessibility = TokenKind.Private;
            bool isStatic = false;
            bool isConst = false;
            bool isReadonly = false;
            bool isVirtual = false;
            bool isOverride = false;
            bool isAbstract = false;
            FunctionImplementationKind implementation = FunctionImplementationKind.Ordinary;
            AstNode Finish(AstNode node) => ApplyImplementationKind(node, implementation);

            while (true)
            {
                if (Check(TokenKind.Public) || Check(TokenKind.Private) ||
                    Check(TokenKind.Protected) || Check(TokenKind.Internal))
                { accessibility = Current.Kind; Consume(); }
                else if (Check(TokenKind.Static)) { isStatic = true; Consume(); }
                else if (Check(TokenKind.Const)) { isConst = true; Consume(); }
                else if (Check(TokenKind.Readonly) && !isReadonly) { isReadonly = true; Consume(); }
                else if (Check(TokenKind.Virtual)) { isVirtual = true; Consume(); }
                else if (Check(TokenKind.Override)) { isOverride = true; Consume(); }
                else if (Check(TokenKind.Abstract)) { isAbstract = true; Consume(); }
                else if (Check(TokenKind.Weak) || Check(TokenKind.Replace))
                {
                    if (implementation != FunctionImplementationKind.Ordinary)
                        throw new TypeCheckException("Use exactly one of weak or replace", Current.Line);
                    implementation = Consume().Kind == TokenKind.Weak ? FunctionImplementationKind.Weak : FunctionImplementationKind.Replace;
                }
                else break;
            }

            if (Check(TokenKind.Class))
                return Finish(ParseClassDeclaration(accessibility, isAbstract, line, attributes));
            if (Check(TokenKind.Struct))
                return Finish(ParseStructDeclaration(accessibility, line, attributes));

            if (Check(TokenKind.Tilde))
            {
                Consume();
                string dtorName = Expect(TokenKind.Identifier).Text;
                Expect(TokenKind.OpenParen);
                Expect(TokenKind.CloseParen);
                BlockStatement dtorBody = ParseBodyOrBlock();
                return Finish(new DestructorDeclaration(dtorName, dtorBody, isVirtual, line));
            }

            if (Check(TokenKind.Alias))
                return Finish(ParseAliasDeclaration(accessibility, line));
            if (Check(TokenKind.Enum))
                return Finish(ParseEnumDeclaration(accessibility, line));

            TypeExpression type = ParseTypeExpression();
            string name;

            if (Check(TokenKind.OpenParen))
            {
                // constructor - type was actually the name
                name = ((NamedTypeExpression)type).Name;
                return Finish(ParseConstructorDeclaration(name, accessibility, line, attributes, isConst));
            }

            if (Match(TokenKind.Operator))
            {
                if (Check(TokenKind.Identifier))
                {
                    Token prefix = Consume();
                    Token marker = Expect(TokenKind.StringLiteral);
                    if (marker.Text != "\"\"" || prefix.End + 1 != marker.Start)
                        throw new TypeCheckException("A string literal operator must use an identifier immediately followed by empty quotes", line);
                    if (!isStatic || isConst || isVirtual || isOverride || isAbstract || isReadonly)
                        throw new TypeCheckException("String literal operators must be static, non-const, non-virtual methods", line);
                    MethodDeclaration method = ParseMethodDeclaration(type, "$literal$" + prefix.Text, accessibility, true, false, false, false, line, attributes: attributes);
                    method.StringLiteralPrefix = prefix.Text;
                    return Finish(method);
                }
                return Finish(ParseOperatorDeclaration(type, accessibility, true, line));
            }

            name = Expect(TokenKind.Identifier).Text;
            List<GenericParameter> genericParams = ParseGenericParameters();

            // if followed by ( it's a method, otherwise a field
            if (Check(TokenKind.OpenParen))
                return Finish(ParseMethodDeclaration(type, name, accessibility, isStatic, isVirtual, isOverride, isAbstract, line, isReadonly, isConst, attributes, genericParams));

            return Finish(ParseFieldDeclaration(type, name, accessibility, isStatic, isConst, isReadonly, line));
        }

        private Parameter ParseParameter() => Located(() => ParseParameterCoreLocated());

        private Parameter ParseParameterCoreLocated()
        {
            int line = Current.Line;
            bool isConst = Match(TokenKind.Const);
            TypeExpression paramType = ParseTypeExpression();
            string paramName = Expect(TokenKind.Identifier).Text;
            return new Parameter(paramName, paramType, line, isConst);
        }

        private List<AttributeNode> ParseAttributes()
        {
            List<AttributeNode> attributes = new();
            while (Check(TokenKind.OpenBracket))
            {
                int line = Current.Line;
                Consume(); // [
                string name = Expect(TokenKind.Identifier).Text;
                while (Match(TokenKind.DoubleColon))
                    name += "::" + Expect(TokenKind.Identifier).Text;
                List<AstNode> args = new();
                if (Match(TokenKind.OpenParen))
                {
                    while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                    {
                        args.Add(ParseExpression());
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

        private ExternDeclaration ParseExternDeclaration(List<AttributeNode> attributes, int line) => Located(() => ParseExternDeclarationCoreLocated(attributes, line));

        private ExternDeclaration ParseExternDeclarationCoreLocated(List<AttributeNode> attributes, int line)
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
                parameters.Add(ParseParameter());
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }

            Expect(TokenKind.CloseParen);
            Expect(TokenKind.Semicolon);
            return new ExternDeclaration(name, returnType, parameters, isVariadic, attributes, line);
        }

        private ConstructorDeclaration ParseConstructorDeclaration(string name, TokenKind accessibility, int line, List<AttributeNode>? attributes = null, bool isConst = false) => Located(() => ParseConstructorDeclarationCoreLocated(name, accessibility, line, attributes, isConst));

        private ConstructorDeclaration ParseConstructorDeclarationCoreLocated(string name, TokenKind accessibility, int line, List<AttributeNode>? attributes = null, bool isConst = false)
        {
            Expect(TokenKind.OpenParen);
            List<Parameter> parameters = new();
            while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
            {
                parameters.Add(ParseParameter());
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }
            Expect(TokenKind.CloseParen);

            List<AstNode>? baseArguments = null;
            if (Match(TokenKind.Colon))
            {
                if (Check(TokenKind.Base))
                {
                    Consume();
                }
                else if (Check(TokenKind.Identifier))
                {
                    Consume();
                }
                else
                {
                    _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, Current.Line, Current.Start, null, $"Expected 'base' or base class name after ':' in constructor initializer on line {Current.Line}");
                }

                Expect(TokenKind.OpenParen);
                baseArguments = new List<AstNode>();
                while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                {
                    baseArguments.Add(ParseExpression());
                    if (!Check(TokenKind.CloseParen))
                        Expect(TokenKind.Comma);
                }
                Expect(TokenKind.CloseParen);
            }

            BlockStatement body = ParseBodyOrBlock();
            return new ConstructorDeclaration(name, parameters, body, accessibility, line, baseArguments, attributes, isConst);
        }

        private BlockStatement ParseBodyOrBlock() => Located(() => ParseBodyOrBlockCoreLocated());

        private BlockStatement ParseBodyOrBlockCoreLocated()
        {
            int line = Current.Line;
            if (Check(TokenKind.OpenBrace))
                return ParseBlockStatement();

            if (Match(TokenKind.EqualsGreater))
            {
                AstNode expr = ParseExpression();
                Expect(TokenKind.Semicolon);
                return new BlockStatement(new List<AstNode> { new ReturnStatement(expr, line) }, line);
            }

            AstNode statement = ParseStatement();
            return new BlockStatement(new List<AstNode> { statement }, line);
        }

        private static bool IsOverloadableOperator(TokenKind kind) => kind switch
        {
            TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent or
            TokenKind.EqualsEquals or TokenKind.NotEquals or TokenKind.Less or TokenKind.Greater or
            TokenKind.LessEquals or TokenKind.GreaterEquals or TokenKind.Ampersand or TokenKind.Pipe or
            TokenKind.Caret or TokenKind.LessLess or TokenKind.GreaterGreater or TokenKind.Bang => true,
            _ => false
        };

        private OperatorDeclaration ParseOperatorDeclaration(TypeExpression returnType, TokenKind accessibility, bool isStatic, int line) => Located(() => ParseOperatorDeclarationCoreLocated(returnType, accessibility, isStatic, line));

        private OperatorDeclaration ParseOperatorDeclarationCoreLocated(TypeExpression returnType, TokenKind accessibility, bool isStatic, int line)
        {
            Token opToken = Current;
            if (!IsOverloadableOperator(opToken.Kind))
            {
                _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, opToken.Line, opToken.Start, null, $"Expected overloadable operator after 'operator', got '{opToken.Text}' on line {opToken.Line}");
            }
            Consume();
            string opSymbol = opToken.Text;

            Expect(TokenKind.OpenParen);
            List<Parameter> parameters = new();
            while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
            {
                parameters.Add(ParseParameter());
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }
            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new OperatorDeclaration(opToken.Kind, opSymbol, returnType, parameters, body, accessibility, isStatic, line);
        }

        private MethodDeclaration ParseMethodDeclaration(TypeExpression returnType, string name, TokenKind accessibility, bool isStatic, bool isVirtual, bool isOverride, bool isAbstract, int line, bool isReadOnly = false, bool isConst = false, List<AttributeNode>? attributes = null, List<GenericParameter>? genericParameters = null) => Located(() => ParseMethodDeclarationCoreLocated(returnType, name, accessibility, isStatic, isVirtual, isOverride, isAbstract, line, isReadOnly, isConst, attributes, genericParameters));

        private MethodDeclaration ParseMethodDeclarationCoreLocated(TypeExpression returnType, string name, TokenKind accessibility, bool isStatic, bool isVirtual, bool isOverride, bool isAbstract, int line, bool isReadOnly = false, bool isConst = false, List<AttributeNode>? attributes = null, List<GenericParameter>? genericParameters = null)
        {
            if (isReadOnly)
            {
                if (returnType is PointerTypeExpression ptr && !ptr.IsReadOnly)
                {
                    returnType = new PointerTypeExpression(ptr.Inner, ptr.IsNullable, ptr.Line, isReadOnly: true);
                    isReadOnly = false;
                }
                else if (returnType is ManagedTypeExpression mgd && !mgd.IsReadOnly)
                {
                    returnType = new ManagedTypeExpression(mgd.Inner, mgd.IsNullable, mgd.Line, isReadOnly: true);
                    isReadOnly = false;
                }
            }

            Expect(TokenKind.OpenParen);
            List<Parameter> parameters = new();
            while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
            {
                parameters.Add(ParseParameter());
                if (!Check(TokenKind.CloseParen))
                    Expect(TokenKind.Comma);
            }
            Expect(TokenKind.CloseParen);
            bool throws = Match(TokenKind.Throws);
            BlockStatement? body = null;
            if (isAbstract)
            {
                Expect(TokenKind.Semicolon);
            }
            else
            {
                body = Match(TokenKind.Semicolon) ? null : ParseBodyOrBlock();
            }
            return new MethodDeclaration(name, returnType, parameters, body, accessibility, isStatic, isVirtual, isOverride, isAbstract, line, isReadOnly, isConst, attributes, genericParameters, throws: throws);
        }

        private FieldDeclaration ParseFieldDeclaration(TypeExpression type, string name, TokenKind accessibility, bool isStatic, bool isConst, bool isReadonly, int line) => Located(() => ParseFieldDeclarationCoreLocated(type, name, accessibility, isStatic, isConst, isReadonly, line));

        private FieldDeclaration ParseFieldDeclarationCoreLocated(TypeExpression type, string name, TokenKind accessibility, bool isStatic, bool isConst, bool isReadonly, int line)
        {
            AstNode? initializer = null;
            if (Match(TokenKind.Equals))
                initializer = ParseExpression();
            Expect(TokenKind.Semicolon);
            if (isReadonly)
            {
                if (type is PointerTypeExpression ptr && !ptr.IsReadOnly)
                {
                    type = new PointerTypeExpression(ptr.Inner, ptr.IsNullable, ptr.Line, isReadOnly: true);
                }
                else if (type is ManagedTypeExpression mgd && !mgd.IsReadOnly)
                {
                    type = new ManagedTypeExpression(mgd.Inner, mgd.IsNullable, mgd.Line, isReadOnly: true);
                }
            }
            return new FieldDeclaration(name, type, initializer, accessibility, isConst, isStatic, line, isReadonly);
        }

        private TypeExpression ParseTypeExpression() => Located(() => ParseTypeExpressionCoreLocated());

        private TypeExpression ParseTypeExpressionCoreLocated()
        {
            int line = Current.Line;
            bool isReadOnly = Match(TokenKind.Readonly);
            string name = "";

            // handle built-in type keywords
            if (Current.Kind is TokenKind.Int or TokenKind.UInt or TokenKind.Long or TokenKind.ULong or
                TokenKind.NInt or TokenKind.NUInt or TokenKind.Float or TokenKind.Bool or
                TokenKind.Char or TokenKind.Byte or TokenKind.SByte or TokenKind.Short or TokenKind.UShort or TokenKind.ExtraLong or
                TokenKind.String or TokenKind.Void)
            {
                name = Current.Text;
                Consume();
            }
            else
            {
                name = Expect(TokenKind.Identifier).Text;
            }

            List<TypeExpression> typeArgs = new();
            if (Check(TokenKind.Less))
            {
                Consume(); // <
                while (!Check(TokenKind.Greater) && !Check(TokenKind.EndOfFile))
                {
                    typeArgs.Add(ParseTypeExpression());
                    if (!Check(TokenKind.Greater))
                        Expect(TokenKind.Comma);
                }
                Expect(TokenKind.Greater);
            }

            TypeExpression type = new NamedTypeExpression(name, null, line, typeArgs);

            if (Check(TokenKind.Dot))
            {
                throw new TypeCheckException($"Type names must use '::' for scoping and nested types, not '.' on line {Current.Line}", Current.Line);
            }

            while (Match(TokenKind.DoubleColon))
            {
                int memberLine = Current.Line;
                string member = Expect(TokenKind.Identifier).Text;
                List<TypeExpression> memberTypeArgs = new();
                if (Check(TokenKind.Less))
                {
                    Consume();
                    while (!Check(TokenKind.Greater) && !Check(TokenKind.EndOfFile))
                    {
                        memberTypeArgs.Add(ParseTypeExpression());
                        if (!Check(TokenKind.Greater))
                            Expect(TokenKind.Comma);
                    }
                    Expect(TokenKind.Greater);
                }
                type = new NestedTypeExpression(type, member, memberTypeArgs, memberLine);

                if (Check(TokenKind.Dot))
                {
                    throw new TypeCheckException($"Type names must use '::' for scoping and nested types, not '.' on line {Current.Line}", Current.Line);
                }
            }

            // postfix modifiers
            while (true)
            {
                if (Match(TokenKind.Star))
                {
                    bool nullable = Match(TokenKind.QuestionMark);
                    type = new PointerTypeExpression(type, nullable, line, isReadOnly);
                    isReadOnly = false;
                }
                else if (Match(TokenKind.Caret))
                {
                    bool nullable = Match(TokenKind.QuestionMark);
                    type = new ManagedTypeExpression(type, nullable, line, isReadOnly);
                    isReadOnly = false;
                }
                else if (Check(TokenKind.OpenParen))
                {
                    int saved = _pos;
                    var savedTokens = new List<Token>(_tokens);
                    int diagMark = _diagnostics.Count;
                    bool isFnPtr = false;
                    List<TypeExpression> paramTypes = new();
                    bool isManaged = false;
                    bool nullable = false;
                    try
                    {
                        Consume(); // (
                        while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                        {
                            paramTypes.Add(ParseTypeExpression());
                            if (!Check(TokenKind.CloseParen))
                                Expect(TokenKind.Comma);
                        }
                        Expect(TokenKind.CloseParen);

                        if (Match(TokenKind.Star))
                        {
                            isManaged = false;
                            isFnPtr = true;
                        }
                        else if (Match(TokenKind.Caret))
                        {
                            isManaged = true;
                            isFnPtr = true;
                        }

                        if (isFnPtr)
                            nullable = Match(TokenKind.QuestionMark);
                    }
                    catch
                    {
                        isFnPtr = false;
                    }

                    if (isFnPtr)
                    {
                        type = new FunctionPointerTypeExpression(type, paramTypes, isManaged, nullable, line);
                    }
                    else
                    {
                        _diagnostics.ClearSince(diagMark);
                        _pos = saved;
                        _tokens = savedTokens;
                        break;
                    }
                }
                else if (Match(TokenKind.OpenBracket))
                {
                    int? size = null;
                    AstNode? sizeExpr = null;
                    if (!Check(TokenKind.CloseBracket))
                    {
                        sizeExpr = ParseExpression();
                    }
                    Expect(TokenKind.CloseBracket);
                    type = new ArrayTypeExpression(type, size, line, sizeExpr);
                }
                else
                {
                    break;
                }
            }

            return type;
        }

        private BlockStatement ParseBlockStatement() => Located(() => ParseBlockStatementCoreLocated());

        private BlockStatement ParseBlockStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.OpenBrace);
            List<AstNode> statements = new();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
                statements.Add(ParseStatement());
            Expect(TokenKind.CloseBrace);
            return new BlockStatement(statements, line);
        }

        private AstNode ParseStatement() => Located(() => ParseStatementCoreLocated());

        private AstNode ParseStatementCoreLocated()
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
                case TokenKind.Foreach:
                    return ParseForeachStatement();
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
                case TokenKind.Defer:
                    return ParseDeferStatement();
                case TokenKind.Delete:
                    return ParseDeleteStatement();
                case TokenKind.Throw:
                    return ParseThrowStatement();
                case TokenKind.Try:
                    return ParseTryStatement();
                case TokenKind.Alias:
                    return ParseAliasDeclaration(TokenKind.Private, line);
                case TokenKind.Enum:
                    return ParseEnumDeclaration(TokenKind.Private, line);
            }

            if (Check(TokenKind.Const))
            {
                Consume();
                return ParseVariableDeclaration(isConst: true);
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
            Current.Kind is TokenKind.Byte or TokenKind.SByte or TokenKind.Short or TokenKind.UShort or
            TokenKind.Int or TokenKind.UInt or TokenKind.Long or TokenKind.ULong or
            TokenKind.NInt or TokenKind.NUInt or TokenKind.Float or TokenKind.Bool or
            TokenKind.Char or TokenKind.ExtraLong or
            TokenKind.String or TokenKind.Void or TokenKind.Identifier or TokenKind.Readonly;

        private bool IsVariableDeclaration()
        {
            // look ahead to see if this is "type name" or "type* name" etc
            int saved = _pos;
            var savedTokens = new List<Token>(_tokens);
            int diagMark = _diagnostics.Count;
            try
            {
                ParseTypeExpression();
                bool isVar = _diagnostics.Count == diagMark && Check(TokenKind.Identifier);
                if (!isVar)
                {
                    _diagnostics.ClearSince(diagMark);
                }
                return isVar;
            }
            catch
            {
                _diagnostics.ClearSince(diagMark);
                return false;
            }
            finally
            {
                _pos = saved;
                _tokens = savedTokens;
            }
        }

        private AliasDeclaration ParseAliasDeclaration(TokenKind accessibility, int line) => Located(() => ParseAliasDeclarationCoreLocated(accessibility, line));

        private AliasDeclaration ParseAliasDeclarationCoreLocated(TokenKind accessibility, int line)
        {
            Expect(TokenKind.Alias);
            string name = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.Equals);
            TypeExpression target = ParseTypeExpression();
            Expect(TokenKind.Semicolon);
            return new AliasDeclaration(name, target, accessibility, line);
        }

        private EnumDeclaration ParseEnumDeclaration(TokenKind accessibility, int line) => Located(() => ParseEnumDeclarationCoreLocated(accessibility, line));

        private EnumDeclaration ParseEnumDeclarationCoreLocated(TokenKind accessibility, int line)
        {
            Expect(TokenKind.Enum);
            string name = Expect(TokenKind.Identifier).Text;

            TypeExpression? underlyingType = null;
            if (Match(TokenKind.Colon))
            {
                underlyingType = ParseTypeExpression();
            }

            Expect(TokenKind.OpenBrace);
            List<EnumMemberDeclaration> members = new List<EnumMemberDeclaration>();
            while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
            {
                int memberLine = Current.Line;
                string memberName = Expect(TokenKind.Identifier).Text;
                AstNode? memberValue = null;
                if (Match(TokenKind.Equals))
                {
                    memberValue = ParseExpression();
                }
                members.Add(new EnumMemberDeclaration(memberName, memberValue, memberLine));

                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.CloseBrace);

            return new EnumDeclaration(name, underlyingType, members, accessibility, line);
        }

        private DeferStatement ParseDeferStatement() => Located(() => ParseDeferStatementCoreLocated());

        private DeferStatement ParseDeferStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Defer);
            AstNode stmt = ParseStatement();
            return new DeferStatement(stmt, line);
        }

        private DeleteStatement ParseDeleteStatement() => Located(() => ParseDeleteStatementCoreLocated());

        private DeleteStatement ParseDeleteStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Delete);
            AstNode expr = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new DeleteStatement(expr, line);
        }

        private ThrowStatement ParseThrowStatement() => Located(() => ParseThrowStatementCoreLocated());

        private ThrowStatement ParseThrowStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Throw);
            AstNode expr = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new ThrowStatement(expr, line);
        }

        private TryStatement ParseTryStatement() => Located(() => ParseTryStatementCoreLocated());

        private TryStatement ParseTryStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Try);
            BlockStatement tryBlock = ParseBlockStatement();
            List<CatchClause> catchClauses = new();

            while (Match(TokenKind.Catch))
            {
                int catchLine = Current.Line;
                TypeExpression? exType = null;
                string? varName = null;

                if (Match(TokenKind.OpenParen))
                {
                    exType = ParseTypeExpression();
                    if (Check(TokenKind.Identifier))
                    {
                        varName = Expect(TokenKind.Identifier).Text;
                    }
                    Expect(TokenKind.CloseParen);
                }

                BlockStatement catchBody = ParseBlockStatement();
                catchClauses.Add(new CatchClause(exType, varName, catchBody, catchLine));
            }

            if (catchClauses.Count == 0)
            {
                _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, line, 0, null, $"Expected at least one catch clause after try block on line {line}");
            }

            return new TryStatement(tryBlock, catchClauses, line);
        }

        private ReturnStatement ParseReturnStatement() => Located(() => ParseReturnStatementCoreLocated());

        private ReturnStatement ParseReturnStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Return);
            AstNode? value = null;
            if (!Check(TokenKind.Semicolon))
                value = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new ReturnStatement(value, line);
        }

        private IfStatement ParseIfStatement() => Located(() => ParseIfStatementCoreLocated());

        private IfStatement ParseIfStatementCoreLocated()
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

        private WhileStatement ParseWhileStatement() => Located(() => ParseWhileStatementCoreLocated());

        private WhileStatement ParseWhileStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.While);
            Expect(TokenKind.OpenParen);
            AstNode condition = ParseExpression();
            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new WhileStatement(condition, body, line);
        }

        private ForStatement ParseForStatement() => Located(() => ParseForStatementCoreLocated());

        private ForStatement ParseForStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.For);
            Expect(TokenKind.OpenParen);

            AstNode? initializer = null;
            if (!Check(TokenKind.Semicolon))
            {
                if (Check(TokenKind.Const))
                {
                    Consume();
                    initializer = ParseVariableDeclaration(isConst: true);
                }
                else
                {
                    initializer = IsVariableDeclaration() ? ParseVariableDeclaration() : ParseExpression();
                }
            }
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

        private ForeachStatement ParseForeachStatement() => Located(() => ParseForeachStatementCoreLocated());

        private ForeachStatement ParseForeachStatementCoreLocated()
        {
            int line = Current.Line;
            Expect(TokenKind.Foreach);
            Expect(TokenKind.OpenParen);
            TypeExpression elementType = ParseTypeExpression();
            if (Check(TokenKind.In))
            {
                _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, line, 0, null, $"Foreach loop requires an explicit type for loop variable on line {line}");
            }
            string variableName = Expect(TokenKind.Identifier).Text;
            Expect(TokenKind.In);
            AstNode collection = ParseExpression();
            Expect(TokenKind.CloseParen);
            BlockStatement body = ParseBodyOrBlock();
            return new ForeachStatement(elementType, variableName, collection, body, line);
        }

        private VariableDeclaration ParseVariableDeclaration(bool isConst = false) => Located(() => ParseVariableDeclarationCoreLocated(isConst));

        private VariableDeclaration ParseVariableDeclarationCoreLocated(bool isConst = false)
        {
            int line = Current.Line;
            TypeExpression type = ParseTypeExpression();
            string name = Expect(TokenKind.Identifier).Text;
            AstNode? initializer = null;
            if (Match(TokenKind.Equals))
                initializer = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new VariableDeclaration(name, type, initializer, line, isConst);
        }

        private AstNode ParseExpression(int minBindingPower = 0) => Located(() => ParseExpressionCoreLocated(minBindingPower));

        private AstNode ParseExpressionCoreLocated(int minBindingPower = 0)
        {
            AstNode left = ParsePrefix();

            while (true)
            {
                if (Check(TokenKind.Less) && IsGenericCallAhead())
                {
                    int callLine = Current.Line;
                    Consume(); // <
                    List<TypeExpression> typeArgs = new();
                    while (!Check(TokenKind.Greater) && !Check(TokenKind.EndOfFile))
                    {
                        typeArgs.Add(ParseTypeExpression());
                        if (!Check(TokenKind.Greater))
                            Expect(TokenKind.Comma);
                    }
                    Expect(TokenKind.Greater);
                    Expect(TokenKind.OpenParen);
                    List<AstNode> args = new();
                    while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                    {
                        args.Add(ParseExpression());
                        if (!Check(TokenKind.CloseParen))
                            Expect(TokenKind.Comma);
                    }
                    Expect(TokenKind.CloseParen);
                    left = new CallExpression(left, args, callLine, typeArgs);
                    continue;
                }

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

                // handle indexing
                if (op.Kind == TokenKind.OpenBracket)
                {
                    AstNode index = ParseExpression();
                    Expect(TokenKind.CloseBracket);
                    left = new IndexExpression(left, index, op.Line);
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
                    Token memberToken = Expect(TokenKind.Identifier);
                    if (Check(TokenKind.StringLiteral) && memberToken.End + 1 == Current.Start)
                    {
                        Token strToken = Consume();
                        left = new PrefixedStringLiteralExpression(left, memberToken.Text!, new LiteralExpression(strToken, strToken.Line), op.Line);
                        continue;
                    }
                    left = new NamespaceAccessExpression(left, memberToken.Text!, op.Line);
                    continue;
                }

                AstNode rightExpr = ParseExpression(rightPower);
                left = new BinaryExpression(left, rightExpr, op.Kind, op.Line);
            }

            return left;
        }

        private AstNode ParsePrefix() => Located(() => ParsePrefixCoreLocated());

        private AstNode ParsePrefixCoreLocated()
        {
            int line = Current.Line;
            if (Match(TokenKind.OpenBracket))
            {
                List<AstNode> elements = new();
                if (!Check(TokenKind.CloseBracket))
                {
                    do
                    {
                        elements.Add(ParseExpression());
                        if (!Match(TokenKind.Comma)) break;
                    } while (!Check(TokenKind.CloseBracket));
                }
                Expect(TokenKind.CloseBracket);
                return new ArrayLiteralExpression(elements, line);
            }

            // unary prefix operators
            if (Check(TokenKind.Bang) || Check(TokenKind.Minus) || Check(TokenKind.Star) ||
                Check(TokenKind.Ampersand) || Check(TokenKind.PlusPlus) || Check(TokenKind.MinusMinus))
            {
                Token op = Consume();
                AstNode operand = ParseExpression(22); // high binding power for prefix (higher than binary ops)
                return new UnaryExpression(operand, op.Kind, true, line);
            }

            // static lambda
            if (Check(TokenKind.Static))
            {
                int saved = _pos;
                var savedTokens = new List<Token>(_tokens);
                Consume(); // 'static'
                if (Check(TokenKind.OpenParen))
                {
                    LambdaExpression? lambda = TryParseLambda(true);
                    if (lambda != null)
                    {
                        return lambda;
                    }
                }
                _pos = saved;
                _tokens = savedTokens;
            }

            // lambda expression
            if (Check(TokenKind.OpenParen))
            {
                LambdaExpression? lambda = TryParseLambda(false);
                if (lambda != null)
                {
                    return lambda;
                }
            }

            // cast or parenthesized expression
            if (Check(TokenKind.OpenParen))
            {
                int saved = _pos;
                var savedTokens = new List<Token>(_tokens);
                int diagMark = _diagnostics.Count;
                bool isCast = false;
                TypeExpression? castType = null;
                try
                {
                    Consume(); // '('
                    castType = ParseTypeExpression();
                    if (Match(TokenKind.CloseParen))
                    {
                        bool isDefiniteType = IsDefiniteType(castType);
                        if (isDefiniteType && IsCastOperandStarter(Current.Kind))
                        {
                            isCast = true;
                        }
                        else if (!isDefiniteType && IsUnambiguousCastOperandStarter(Current.Kind))
                        {
                            isCast = true;
                        }
                    }
                }
                catch
                {
                    isCast = false;
                }

                if (isCast && castType != null && _diagnostics.Count == diagMark)
                {
                    AstNode operand = ParseExpression(22);
                    return new CastExpression(castType, operand, line);
                }

                // fallback to parenthesized expression
                _diagnostics.ClearSince(diagMark);
                _pos = saved;
                _tokens = savedTokens;
                Consume(); // '('
                AstNode expr = ParseExpression();
                Expect(TokenKind.CloseParen);
                return expr;
            }

            // interpolated string
            if (Check(TokenKind.InterpolatedStringSegment) || Check(TokenKind.InterpolatedStringExprStart))
                return ParseInterpolatedString();

            // literals
            if (Current.Kind is TokenKind.IntLiteral or TokenKind.UIntLiteral or TokenKind.LongLiteral or TokenKind.ULongLiteral or
                TokenKind.FloatLiteral or TokenKind.DoubleLiteral or
                TokenKind.HexInt or TokenKind.StringLiteral or
                TokenKind.CharLiteral or TokenKind.True or TokenKind.False or TokenKind.Null)
            {
                Token token = Consume();
                return new LiteralExpression(token, line);
            }

            // default expression
            if (Check(TokenKind.Default))
            {
                Consume();
                TypeExpression? targetType = null;
                if (Match(TokenKind.OpenParen))
                {
                    targetType = ParseTypeExpression();
                    Expect(TokenKind.CloseParen);
                }
                return new DefaultExpression(targetType, line);
            }

            // sizeof expression
            if (Check(TokenKind.Sizeof))
            {
                Consume();
                Expect(TokenKind.OpenParen);
                TypeExpression targetType = ParseTypeExpression();
                Expect(TokenKind.CloseParen);
                return new SizeofExpression(targetType, line);
            }

            // nameof expression
            if (Check(TokenKind.Nameof))
            {
                Consume();
                Expect(TokenKind.OpenParen);
                AstNode target;
                if (IsBuiltInTypeKeyword(Current.Kind))
                {
                    target = ParseTypeExpression();
                }
                else
                {
                    target = ParseExpression();
                }
                Expect(TokenKind.CloseParen);
                return new NameofExpression(target, line);
            }

            // new expression
            if (Check(TokenKind.New))
            {
                Consume();
                AllocationKind kind = AllocationKind.Value;
                if (Match(TokenKind.Star))
                    kind = AllocationKind.Pointer;
                else if (Match(TokenKind.Caret))
                    kind = AllocationKind.Managed;

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
                return new NewExpression(type, args, kind, line);
            }

            // identifier
            if (Check(TokenKind.Identifier))
            {
                Token token = Consume();
                if (Check(TokenKind.StringLiteral) && token.End + 1 == Current.Start)
                {
                    Token strToken = Consume();
                    return new PrefixedStringLiteralExpression(null, token.Text!, new LiteralExpression(strToken, strToken.Line), line);
                }
                return new IdentifierExpression(token.Text, line);
            }

            // global
            if (Check(TokenKind.Global))
            {
                Consume(); // consume 'global'
                Expect(TokenKind.DoubleColon);
                string member = Expect(TokenKind.Identifier).Text;
                AstNode left = new GlobalExpression(line);
                // handle chained :: after global
                AstNode result = new NamespaceAccessExpression(left, member, line);
                return result;
            }

            _diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, line, Current.Start, null, $"Unexpected token '{Current.Text}' on line {line}");
            Consume();
            return new IdentifierExpression("<error>", line);
        }

        private static bool IsDefiniteType(TypeExpression type)
        {
            if (type is PointerTypeExpression or ManagedTypeExpression or ArrayTypeExpression or FunctionPointerTypeExpression)
                return true;
            if (type is NamedTypeExpression named)
            {
                if (named.Namespace != null)
                    return true;
                return named.Name is "byte" or "sbyte" or "short" or "ushort" or
                    "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or
                    "float" or "double" or "bool" or "char" or "extralong" or "string" or "void";
            }
            return false;
        }

        private static bool IsBuiltInTypeKeyword(TokenKind kind) =>
            kind is TokenKind.Int or TokenKind.UInt or TokenKind.Long or TokenKind.ULong or
            TokenKind.NInt or TokenKind.NUInt or TokenKind.Float or TokenKind.Bool or
            TokenKind.Char or TokenKind.Byte or TokenKind.SByte or TokenKind.Short or TokenKind.UShort or
            TokenKind.ExtraLong or TokenKind.String or TokenKind.Void;

        private static bool IsCastOperandStarter(TokenKind kind) =>
            kind is TokenKind.Identifier or
            TokenKind.IntLiteral or TokenKind.UIntLiteral or TokenKind.LongLiteral or TokenKind.ULongLiteral or
            TokenKind.HexInt or TokenKind.FloatLiteral or TokenKind.DoubleLiteral or
            TokenKind.StringLiteral or TokenKind.CharLiteral or TokenKind.True or TokenKind.False or TokenKind.Null or
            TokenKind.OpenParen or TokenKind.New or TokenKind.Global or TokenKind.Sizeof or TokenKind.Nameof or
            TokenKind.InterpolatedStringSegment or TokenKind.InterpolatedStringExprStart or
            TokenKind.Minus or TokenKind.Bang or TokenKind.Star or TokenKind.Ampersand or
            TokenKind.PlusPlus or TokenKind.MinusMinus;

        private static bool IsUnambiguousCastOperandStarter(TokenKind kind) =>
            kind is TokenKind.Identifier or
            TokenKind.IntLiteral or TokenKind.UIntLiteral or TokenKind.LongLiteral or TokenKind.ULongLiteral or
            TokenKind.HexInt or TokenKind.FloatLiteral or TokenKind.DoubleLiteral or
            TokenKind.StringLiteral or TokenKind.CharLiteral or TokenKind.True or TokenKind.False or TokenKind.Null or
            TokenKind.OpenParen or TokenKind.New or TokenKind.Global or TokenKind.Sizeof or TokenKind.Nameof or
            TokenKind.InterpolatedStringSegment or TokenKind.InterpolatedStringExprStart or
            TokenKind.Bang or TokenKind.PlusPlus or TokenKind.MinusMinus;

        private LambdaExpression? TryParseLambda(bool isStatic) => Located(() => TryParseLambdaCoreLocated(isStatic));

        private LambdaExpression? TryParseLambdaCoreLocated(bool isStatic)
        {
            int saved = _pos;
            var savedTokens = new List<Token>(_tokens);
            int diagMark = _diagnostics.Count;
            try
            {
                int line = Current.Line;
                Expect(TokenKind.OpenParen);
                List<Parameter> parameters = new();
                while (!Check(TokenKind.CloseParen) && !Check(TokenKind.EndOfFile))
                {
                    parameters.Add(ParseParameter());
                    if (!Check(TokenKind.CloseParen))
                    {
                        Expect(TokenKind.Comma);
                    }
                }
                Expect(TokenKind.CloseParen);

                if (!Match(TokenKind.EqualsGreater) || _diagnostics.Count > diagMark)
                {
                    _diagnostics.ClearSince(diagMark);
                    _pos = saved;
                    _tokens = savedTokens;
                    return null;
                }

                AstNode body;
                bool isExpressionBody;
                if (Check(TokenKind.OpenBrace))
                {
                    body = ParseBlockStatement();
                    isExpressionBody = false;
                }
                else
                {
                    body = ParseExpression();
                    isExpressionBody = true;
                }

                return new LambdaExpression(isStatic, parameters, body, isExpressionBody, line);
            }
            catch
            {
                _diagnostics.ClearSince(diagMark);
                _pos = saved;
                _tokens = savedTokens;
                return null;
            }
        }

        private static (int left, int right) GetInfixBindingPower(TokenKind kind) => kind switch
        {
            TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or
            TokenKind.StarEquals or TokenKind.SlashEquals or TokenKind.PercentEquals => (1, 1),
            TokenKind.PipePipe => (2, 3),
            TokenKind.AmpersandAmpersand => (4, 5),
            TokenKind.Pipe => (6, 7),
            TokenKind.Caret => (8, 9),
            TokenKind.Ampersand => (10, 11),
            TokenKind.EqualsEquals or TokenKind.NotEquals => (12, 13),
            TokenKind.Less or TokenKind.Greater or
            TokenKind.LessEquals or TokenKind.GreaterEquals => (14, 15),
            TokenKind.LessLess or TokenKind.GreaterGreater => (16, 17),
            TokenKind.Plus or TokenKind.Minus => (18, 19),
            TokenKind.Star or TokenKind.Slash or TokenKind.Percent => (20, 21),
            TokenKind.PlusPlus or TokenKind.MinusMinus => (24, 0),
            TokenKind.Dot or TokenKind.Arrow or TokenKind.DoubleColon => (24, 25),
            TokenKind.OpenParen or TokenKind.OpenBracket => (24, 0),
            _ => (0, 0)
        };

        private InterpolatedStringExpression ParseInterpolatedString() => Located(() => ParseInterpolatedStringCoreLocated());

        private InterpolatedStringExpression ParseInterpolatedStringCoreLocated()
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
