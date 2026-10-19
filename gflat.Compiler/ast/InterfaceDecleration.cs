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
        public bool IsStatic { get; }
        public List<GenericParameter> GenericParameters { get; }
        public bool IsGeneric => GenericParameters.Count > 0;

        public InterfaceDeclaration(string name, List<AstNode> members, TokenKind accessibility, int line, bool isStatic = false, List<GenericParameter>? genericParameters = null) : base(line)
        {
            Name = name;
            SourceName = name;
            Members = members;
            Accessibility = accessibility;
            IsStatic = isStatic;
            GenericParameters = genericParameters ?? new();
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
