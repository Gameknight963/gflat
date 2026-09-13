namespace gflat.ast
{
    public class IndexExpression : AstNode
    {
        public AstNode Target { get; }
        public AstNode Index { get; }

        public IndexExpression(AstNode target, AstNode index, int line) : base(line)
        {
            Target = target;
            Index = index;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
