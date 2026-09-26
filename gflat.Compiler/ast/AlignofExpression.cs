using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class AlignofExpression : AstNode
    {
        public TypeExpression TargetType { get; }

        public AlignofExpression(TypeExpression targetType, int line) : base(line)
        {
            TargetType = targetType;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
