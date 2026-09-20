using System;

namespace gflat.ast
{
    public class DeferStatement : AstNode
    {
        public AstNode Statement { get; }

        public DeferStatement(AstNode statement, int line) : base(line)
        {
            Statement = statement;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
