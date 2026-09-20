using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public abstract class AstNode
    {
        public int Line { get; }
        public SourceSpan Span { get; set; }
        public List<AttributeNode> Attributes { get; set; } = new();
        public AstNode(int line) { Line = line; Span = SourceContext.ForLine(line); }
        public abstract void Accept(IVisitor visitor);
    }
}
