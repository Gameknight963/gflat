using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class CallExpression : AstNode
    {
        public AstNode Callee { get; }
        public List<AstNode> Arguments { get; }

        public CallExpression(AstNode callee, List<AstNode> arguments, int line) : base(line)
        {
            Callee = callee;
            Arguments = arguments;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
