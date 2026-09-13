using System.Collections.Generic;

namespace gflat.ast
{
    public class EnumDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression? UnderlyingType { get; }
        public List<EnumMemberDeclaration> Members { get; }
        public TokenKind Accessibility { get; }

        public EnumDeclaration(string name, TypeExpression? underlyingType, List<EnumMemberDeclaration> members, TokenKind accessibility, int line) : base(line)
        {
            Name = name;
            UnderlyingType = underlyingType;
            Members = members;
            Accessibility = accessibility;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
