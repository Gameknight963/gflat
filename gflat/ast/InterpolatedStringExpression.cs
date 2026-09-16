using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{

    public class InterpolatedStringExpression : AstNode
    {
        public AstNode? Scope { get; }
        public string? Prefix { get; }
        public List<AstNode> Parts { get; }

        public InterpolatedStringExpression(List<AstNode> parts, int line, string? prefix = null, AstNode? scope = null) : base(line)
        {
            Parts = parts;
            Prefix = prefix;
            Scope = scope;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
