namespace gflat.ast
{
    public class StructDeclaration : AstNode
    {
        public string Name { get; }
        public List<string> Interfaces { get; }
        public List<AstNode> Members { get; }
        public TokenKind Accessibility { get; }
        public List<AttributeNode> Attributes { get; }
        public List<GenericParameter> GenericParameters { get; }
        public bool IsGeneric => GenericParameters.Count > 0;

        public StructDeclaration(string name, List<string>? interfaces, List<AstNode> members, TokenKind accessibility, int line, List<AttributeNode>? attributes = null, List<GenericParameter>? genericParameters = null) : base(line)
        {
            Name = name;
            Interfaces = interfaces ?? new List<string>();
            Members = members;
            Accessibility = accessibility;
            Attributes = attributes ?? new List<AttributeNode>();
            GenericParameters = genericParameters ?? new List<GenericParameter>();
        }

        public StructDeclaration(string name, List<AstNode> members, TokenKind accessibility, int line, List<AttributeNode>? attributes = null, List<GenericParameter>? genericParameters = null)
            : this(name, null, members, accessibility, line, attributes, genericParameters)
        {
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
