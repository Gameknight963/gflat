using System;

namespace gflat.ast
{
    public class AliasDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression TargetType { get; }
        public TokenKind Accessibility { get; }

        public AliasDeclaration(string name, TypeExpression targetType, TokenKind accessibility, int line) : base(line)
        {
            Name = name;
            TargetType = targetType;
            Accessibility = accessibility;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
