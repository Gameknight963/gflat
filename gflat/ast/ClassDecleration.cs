namespace gflat.ast
{
    public class ClassDeclaration : AstNode
    {
        public string Name { get; }
        public string? BaseClass { get; }
        public List<string> Interfaces { get; }
        public List<AstNode> Members { get; }
        public TokenKind Accessibility { get; }

        public ClassDeclaration(string name, string? baseClass, List<string> interfaces, List<AstNode> members, TokenKind accessibility, int line) : base(line)
        {
            Name = name;
            BaseClass = baseClass;
            Interfaces = interfaces;
            Members = members;
            Accessibility = accessibility;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
