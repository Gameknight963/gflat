namespace gflat.ast
{
    public class GlobalExpression : AstNode
    {
        public GlobalExpression(int line) : base(line) { }
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
