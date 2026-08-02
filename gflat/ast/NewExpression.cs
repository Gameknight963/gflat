using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class NewExpression : AstNode
    {
        public TypeExpression Type { get; }
        public List<AstNode> Arguments { get; }

        public NewExpression(TypeExpression type, List<AstNode> arguments, int line) : base(line)
        {
            Type = type;
            Arguments = arguments;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
