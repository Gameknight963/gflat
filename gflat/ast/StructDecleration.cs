namespace gflat.ast
{
    public class StructDeclaration : AstNode
    {
        public string Name { get; }
        public List<string> Interfaces { get; }
        public List<AstNode> Members { get; }
        public TokenKind Accessibility { get; }

        public StructDeclaration(string name, List<string>? interfaces, List<AstNode> members, TokenKind accessibility, int line) : base(line)
        {
            Name = name;
            Interfaces = interfaces ?? new List<string>();
            Members = members;
            Accessibility = accessibility;
        }

        public StructDeclaration(string name, List<AstNode> members, TokenKind accessibility, int line)
            : this(name, null, members, accessibility, line)
        {
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
