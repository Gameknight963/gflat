using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class MemberAccessExpression : AstNode
    {
        public AstNode Object { get; }
        public string Member { get; }

        public MemberAccessExpression(AstNode obj, string member, int line) : base(line)
        {
            Object = obj;
            Member = member;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
