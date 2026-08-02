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

        public VariableDeclaration(string name, TypeExpression type, AstNode? initializer, int line) : base(line)
        {
            Name = name;
            Type = type;
            Initializer = initializer;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
