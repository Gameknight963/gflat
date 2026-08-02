using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NamedTypeExpression : TypeExpression
    {
        public string Name { get; }
        public string? Namespace { get; }

        public NamedTypeExpression(string name, string? ns, int line) : base(line)
        {
            Name = name;
            Namespace = ns;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
