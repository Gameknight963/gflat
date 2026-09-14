using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ThrowStatement : AstNode
    {
        public AstNode Expression { get; }

        public ThrowStatement(AstNode expression, int line) : base(line)
        {
            Expression = expression;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
