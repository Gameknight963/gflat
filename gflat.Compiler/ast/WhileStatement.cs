using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class WhileStatement : AstNode
    {
        public AstNode Condition { get; }
        public BlockStatement Body { get; }

        public WhileStatement(AstNode condition, BlockStatement body, int line) : base(line)
        {
            Condition = condition;
            Body = body;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
