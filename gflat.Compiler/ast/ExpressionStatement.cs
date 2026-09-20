using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ExpressionStatement : AstNode
    {
        public AstNode Expression { get; }

        public ExpressionStatement(AstNode expression, int line) : base(line) => Expression = expression;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
