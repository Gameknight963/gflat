using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class FieldDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression Type { get; }
        public AstNode? Initializer { get; }
        public TokenKind Accessibility { get; }
        public bool IsConst { get; }
        public bool IsStatic { get; }

        public FieldDeclaration(string name, TypeExpression type, AstNode? initializer, TokenKind accessibility, bool isConst, bool isStatic, int line) : base(line)
        {
            Name = name;
            Type = type;
            Initializer = initializer;
            Accessibility = accessibility;
            IsConst = isConst;
            IsStatic = isStatic;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
