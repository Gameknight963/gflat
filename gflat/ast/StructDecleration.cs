namespace gflat.ast
{
    public class StructDeclaration : AstNode
    {
        public string Name { get; }
        public List<AstNode> Members { get; }
        public TokenKind Accessibility { get; }

        public StructDeclaration(string name, List<AstNode> members, TokenKind accessibility, int line) : base(line)
        {
            Name = name;
            Members = members;
            Accessibility = accessibility;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
