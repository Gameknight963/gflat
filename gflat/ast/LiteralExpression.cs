using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class LiteralExpression : AstNode
    {
        public Token Token { get; }

        public LiteralExpression(Token token, int line) : base(line) => Token = token;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
