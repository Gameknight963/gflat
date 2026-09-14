using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class PrefixedStringLiteralExpression : AstNode
    {
        public AstNode? Scope { get; }
        public string Prefix { get; }
        public LiteralExpression Literal { get; }

        public PrefixedStringLiteralExpression(AstNode? scope, string prefix, LiteralExpression literal, int line) : base(line)
        {
            Scope = scope;
            Prefix = prefix;
            Literal = literal;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
