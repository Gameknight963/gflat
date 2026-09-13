using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class VariableDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression Type { get; }
        public AstNode? Initializer { get; }
        public bool IsConst { get; }

        public VariableDeclaration(string name, TypeExpression type, AstNode? initializer, int line, bool isConst = false) : base(line)
        {
            Name = name;
            Type = type;
            Initializer = initializer;
            IsConst = isConst;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
