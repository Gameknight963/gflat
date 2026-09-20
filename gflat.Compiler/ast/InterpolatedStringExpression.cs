using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{

    public class InterpolatedStringExpression : AstNode
    {
        public List<AstNode> Parts { get; }

        public InterpolatedStringExpression(List<AstNode> parts, int line) : base(line) => Parts = parts;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
