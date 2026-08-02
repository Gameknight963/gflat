using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class UnaryExpression : AstNode
    {
        public AstNode Operand { get; }
        public TokenKind Operator { get; }
        public bool IsPrefix { get; }

        public UnaryExpression(AstNode operand, TokenKind op, bool isPrefix, int line) : base(line)
        {
            Operand = operand;
            Operator = op;
            IsPrefix = isPrefix;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
