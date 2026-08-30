namespace gflat.ast
{
    public class AttributeNode : AstNode
    {
        public string Name { get; }
        public List<string> Arguments { get; }

        public AttributeNode(string name, List<string> arguments, int line) : base(line)
        {
            Name = name;
            Arguments = arguments;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
