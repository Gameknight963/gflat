namespace gflat.ast
{
    public class NamespaceDeclaration : AstNode
    {
        public string Name { get; }
        public List<AstNode> Members { get; } // classes, structs, interfaces type shit

        public NamespaceDeclaration(string name, List<AstNode> members, int line) : base(line)
        {
            Name = name;
            Members = members;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
