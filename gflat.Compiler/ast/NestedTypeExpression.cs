using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NestedTypeExpression : TypeExpression
    {
        public TypeExpression Parent { get; }
        public string Member { get; }
        public List<TypeExpression> TypeArguments { get; }

        public NestedTypeExpression(TypeExpression parent, string member, List<TypeExpression>? typeArguments, int line) : base(line)
        {
            Parent = parent;
            Member = member;
            TypeArguments = typeArguments ?? new List<TypeExpression>();
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
