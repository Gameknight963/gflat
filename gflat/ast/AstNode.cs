using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public abstract class AstNode
    {
        public int Line { get; }
        public List<AttributeNode> Attributes { get; set; } = new();
        public AstNode(int line) => Line = line;
        public abstract void Accept(IVisitor visitor);
    }
}
