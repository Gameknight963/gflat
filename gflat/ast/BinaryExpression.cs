using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class BinaryExpression : AstNode
    {
        public AstNode Left { get; }
        public AstNode Right { get; }
        public TokenKind Operator { get; }

        public BinaryExpression(AstNode left, AstNode right, TokenKind op, int line) : base(line)
        {
            Left = left;
            Right = right;
            Operator = op;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
