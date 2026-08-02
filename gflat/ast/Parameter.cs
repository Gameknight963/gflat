using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class Parameter : AstNode
    {
        public string Name { get; }
        public TypeExpression Type { get; }

        public Parameter(string name, TypeExpression type, int line) : base(line)
        {
            Name = name;
            Type = type;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
