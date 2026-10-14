using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NameofExpression : AstNode
    {
        public AstNode Target { get; }
        // Specialization substitutes types without changing the source name returned.
        public IReadOnlyDictionary<string, TypeExpression> TypeBindings { get; init; } = new Dictionary<string, TypeExpression>();

        public NameofExpression(AstNode target, int line) : base(line)
        {
            Target = target;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
