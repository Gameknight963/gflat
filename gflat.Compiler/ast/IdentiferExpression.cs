using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class IdentifierExpression : AstNode
    {
        public string Name { get; }

        public IdentifierExpression(string name, int line) : base(line) => Name = name;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
