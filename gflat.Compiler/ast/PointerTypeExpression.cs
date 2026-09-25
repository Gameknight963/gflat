using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class PointerTypeExpression : TypeExpression
    {
        public TypeExpression Inner { get; }
        public bool IsNullable { get; }
        public bool IsReadOnly { get; }

        public PointerTypeExpression(TypeExpression inner, bool isNullable, int line, bool isReadOnly = false) : base(line)
        {
            Inner = isReadOnly ? TypeQualifiers.ReadOnly(inner) : inner;
            IsNullable = isNullable;
            IsReadOnly = isReadOnly || inner.IsReadOnlyValue;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
