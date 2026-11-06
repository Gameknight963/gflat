using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public abstract class AstNode
    {
        public int Line { get; }
        public SourceSpan Span { get; set; }
        private List<AttributeNode>? attributes;
        public List<AttributeNode> Attributes
        {
            get => attributes ??= new();
            set => attributes = value;
        }
        // Code-generation hint on callable declarations (including lowered accessors).
        public bool IsInline { get; set; }
        public AstNode(int line) { Line = line; Span = SourceContext.ForLine(line); }
        public abstract void Accept(IVisitor visitor);
    }
}
