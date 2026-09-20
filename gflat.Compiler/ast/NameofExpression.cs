using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NameofExpression : AstNode
    {
        public AstNode Target { get; }

        public NameofExpression(AstNode target, int line) : base(line)
        {
            Target = target;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
