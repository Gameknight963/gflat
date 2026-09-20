using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ReturnStatement : AstNode
    {
        public AstNode? Value { get; }

        public ReturnStatement(AstNode? value, int line) : base(line) => Value = value;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }

}
