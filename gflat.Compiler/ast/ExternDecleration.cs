namespace gflat.ast
{
    public class ExternDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression ReturnType { get; }
        public List<Parameter> Parameters { get; }
        public bool IsVariadic { get; }
        public TokenKind Accessibility { get; }

        public ExternDeclaration(string name, TypeExpression returnType, List<Parameter> parameters, bool isVariadic, List<AttributeNode> attributes, int line, TokenKind accessibility = TokenKind.Public) : base(line)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            IsVariadic = isVariadic;
            Accessibility = accessibility;
            Attributes = attributes;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
