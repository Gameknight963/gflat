using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class AssignmentExpression : AstNode
    {
        public AstNode Target { get; }
        public AstNode Value { get; }
        public TokenKind Operator { get; } // =, +=, -=, etc

        public AssignmentExpression(AstNode target, AstNode value, TokenKind op, int line) : base(line)
        {
            Target = target;
            Value = value;
            Operator = op;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
