using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ContinueStatement : AstNode
    {
        public ContinueStatement(int line) : base(line) { }
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
