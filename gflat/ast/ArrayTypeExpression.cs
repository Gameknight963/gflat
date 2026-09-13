using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ArrayTypeExpression : TypeExpression
    {
        public TypeExpression ElementType { get; }
        public int? Size { get; }

        public ArrayTypeExpression(TypeExpression elementType, int? size, int line) : base(line)
        {
            ElementType = elementType;
            Size = size;
        }

        public ArrayTypeExpression(TypeExpression elementType, int line) : this(elementType, null, line)
        {
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
