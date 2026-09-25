namespace gflat.ast
{
    public class IndexExpression : AstNode
    {
        public AstNode Target { get; }
        public List<AstNode> Indices { get; }
        public AstNode Index => Indices[0];

        public IndexExpression(AstNode target, AstNode index, int line) : this(target, new List<AstNode> { index }, line) { }

        public IndexExpression(AstNode target, List<AstNode> indices, int line) : base(line)
        {
            Target = target;
            Indices = indices;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
