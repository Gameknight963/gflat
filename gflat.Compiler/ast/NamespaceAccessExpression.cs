using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NamespaceAccessExpression : AstNode
    {
        public AstNode Left { get; }
        public string Member { get; }

        public NamespaceAccessExpression(AstNode left, string member, int line) : base(line)
        {
            Left = left;
            Member = member;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
