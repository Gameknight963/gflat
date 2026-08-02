using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class IfStatement : AstNode
    {
        public AstNode Condition { get; }
        public BlockStatement Then { get; }
        public BlockStatement? Else { get; }

        public IfStatement(AstNode condition, BlockStatement then, BlockStatement? else_, int line) : base(line)
        {
            Condition = condition;
            Then = then;
            Else = else_;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
