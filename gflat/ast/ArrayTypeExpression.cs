using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ArrayTypeExpression : TypeExpression
    {
        public TypeExpression ElementType { get; }
        public int? Size { get; }
        public AstNode? SizeExpression { get; }

        public ArrayTypeExpression(TypeExpression elementType, int? size, int line, AstNode? sizeExpression = null) : base(line)
        {
            ElementType = elementType;
            Size = size;
            SizeExpression = sizeExpression;
        }

        public ArrayTypeExpression(TypeExpression elementType, AstNode sizeExpression, int line) : base(line)
        {
            ElementType = elementType;
            Size = null;
            SizeExpression = sizeExpression;
        }

        public ArrayTypeExpression(TypeExpression elementType, int line) : this(elementType, (int?)null, line, (AstNode?)null)
        {
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
