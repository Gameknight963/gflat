namespace gflat.ast
{
    public class AttributeNode : AstNode
    {
        public string Name { get; }
        public List<AstNode> Arguments { get; }

        public AttributeNode(string name, List<AstNode> arguments, int line) : base(line)
        {
            Name = name;
            Arguments = arguments;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
