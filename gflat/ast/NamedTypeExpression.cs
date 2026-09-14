using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NamedTypeExpression : TypeExpression
    {
        public string Name { get; }
        public string? Namespace { get; }
        public List<TypeExpression> TypeArguments { get; }

        public NamedTypeExpression(string name, string? ns, int line, List<TypeExpression>? typeArguments = null) : base(line)
        {
            Name = name;
            Namespace = ns;
            TypeArguments = typeArguments ?? new List<TypeExpression>();
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
