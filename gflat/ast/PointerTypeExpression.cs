using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class PointerTypeExpression : TypeExpression
    {
        public TypeExpression Inner { get; }
        public bool IsNullable { get; }

        public PointerTypeExpression(TypeExpression inner, bool isNullable, int line) : base(line)
        {
            Inner = inner;
            IsNullable = isNullable;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
