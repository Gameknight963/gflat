using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class Parameter : AstNode
    {
        public string Name { get; }
        public TypeExpression Type { get; }
        public bool IsConst { get; }

        public Parameter(string name, TypeExpression type, int line, bool isConst = false) : base(line)
        {
            Name = name;
            Type = type;
            IsConst = isConst;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
