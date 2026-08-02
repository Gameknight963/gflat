using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class MemberAccessExpression : AstNode
    {
        public AstNode Object { get; }
        public string Member { get; }
        public bool IsArrow { get; } // true for ->, false for .

        public MemberAccessExpression(AstNode obj, string member, bool isArrow, int line) : base(line)
        {
            Object = obj;
            Member = member;
            IsArrow = isArrow;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
