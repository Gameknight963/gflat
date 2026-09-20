using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class BreakStatement : AstNode
    {
        public BreakStatement(int line) : base(line) { }
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
