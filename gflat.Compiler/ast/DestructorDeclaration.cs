using System;
using System.Collections.Generic;

namespace gflat.ast
{
    public class DestructorDeclaration : AstNode
    {
        public string Name { get; internal set; }
        public BlockStatement Body { get; }
        public bool IsVirtual { get; }

        public DestructorDeclaration(string name, BlockStatement body, bool isVirtual, int line) : base(line)
        {
            Name = name;
            Body = body;
            IsVirtual = isVirtual;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
