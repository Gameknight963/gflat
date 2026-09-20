namespace gflat.ast
{
    public class UsingDirective : AstNode
    {
        public string Name { get; }

        public UsingDirective(string name, int line) : base(line) => Name = name;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
