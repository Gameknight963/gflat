using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ConstructorDeclaration : AstNode
    {
        public string Name { get; internal set; }
        public List<Parameter> Parameters { get; }
        public BlockStatement Body { get; }
        public TokenKind Accessibility { get; }
        public List<AstNode>? BaseArguments { get; }

        public bool IsConst { get; }

        public ConstructorDeclaration(string name, List<Parameter> parameters, BlockStatement body, TokenKind accessibility, int line, List<AstNode>? baseArguments = null, List<AttributeNode>? attributes = null, bool isConst = false) : base(line)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            Accessibility = accessibility;
            BaseArguments = baseArguments;
            Attributes = attributes ?? new List<AttributeNode>();
            IsConst = isConst;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
