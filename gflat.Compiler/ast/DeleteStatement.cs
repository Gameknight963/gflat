namespace gflat.ast
{
    public class DeleteStatement : AstNode
    {
        public AstNode Target { get; set; }

        public DeleteStatement(AstNode target, int line) : base(line)
        {
            Target = target;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
