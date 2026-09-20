namespace gflat.ast
{
    public class CastExpression : AstNode
    {
        public TypeExpression TargetType { get; }
        public AstNode Operand { get; }

        public CastExpression(TypeExpression targetType, AstNode operand, int line) : base(line)
        {
            TargetType = targetType;
            Operand = operand;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
