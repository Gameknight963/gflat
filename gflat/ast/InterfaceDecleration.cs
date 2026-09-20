using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class InterfaceDeclaration : AstNode
    {
        public string Name { get; internal set; }
        public string SourceName { get; }
        public List<AstNode> Members { get; }
        public TokenKind Accessibility { get; }

        public InterfaceDeclaration(string name, List<AstNode> members, TokenKind accessibility, int line) : base(line)
        {
            Name = name;
            SourceName = name;
            Members = members;
            Accessibility = accessibility;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
