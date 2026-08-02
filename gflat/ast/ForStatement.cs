using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ForStatement : AstNode
    {
        public AstNode? Initializer { get; }
        public AstNode? Condition { get; }
        public AstNode? Increment { get; }
        public BlockStatement Body { get; }

        public ForStatement(AstNode? initializer, AstNode? condition, AstNode? increment, BlockStatement body, int line) : base(line)
        {
            Initializer = initializer;
            Condition = condition;
            Increment = increment;
            Body = body;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
