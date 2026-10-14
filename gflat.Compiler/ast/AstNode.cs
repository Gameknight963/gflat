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
        // Code-generation hint on callable declarations (including lowered accessors).
        public bool IsInline { get; set; }
        public AstNode(int line) { Line = line; Span = SourceContext.ForLine(line); }
        public abstract void Accept(IVisitor visitor);
    }
}
