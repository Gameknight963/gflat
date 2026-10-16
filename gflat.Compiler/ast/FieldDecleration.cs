using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class FieldDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression Type { get; set; }
        public AstNode? Initializer { get; }
        public TokenKind Accessibility { get; }
        public bool IsConst { get; }
        public bool IsStatic { get; }
        public bool IsReadOnly { get; }
        // Source property for generated backing storage; ordinary fields leave this unset.
        public string? PropertyName { get; init; }

        public FieldDeclaration(string name, TypeExpression type, AstNode? initializer, TokenKind accessibility, bool isConst, bool isStatic, int line, bool isReadOnly = false) : base(line)
        {
            Name = name;
            Type = type;
            Initializer = initializer;
            Accessibility = accessibility;
            IsConst = isConst;
            IsStatic = isStatic;
            IsReadOnly = isReadOnly;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
