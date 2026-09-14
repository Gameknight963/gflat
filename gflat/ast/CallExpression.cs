using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class CallExpression : AstNode
    {
        public AstNode Callee { get; }
        public List<AstNode> Arguments { get; }
        public List<TypeExpression> TypeArguments { get; }

        public CallExpression(AstNode callee, List<AstNode> arguments, int line, List<TypeExpression>? typeArguments = null) : base(line)
        {
            Callee = callee;
            Arguments = arguments;
            TypeArguments = typeArguments ?? new List<TypeExpression>();
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
