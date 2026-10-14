using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class Parameter : AstNode
    {
        public string Name { get; }
        public TypeExpression Type { get; set; }
        public bool IsConst { get; }
        public AstNode? DefaultValue { get; }
        public bool IsExtensionReceiver { get; set; }

        public Parameter(string name, TypeExpression type, int line, bool isConst = false, AstNode? defaultValue = null) : base(line)
        {
            Name = name;
            Type = type;
            IsConst = isConst;
            DefaultValue = defaultValue;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
