using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ArrayTypeExpression : TypeExpression
    {
        public TypeExpression ElementType { get; }

        public ArrayTypeExpression(TypeExpression elementType, int line) : base(line)
            => ElementType = elementType;

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
