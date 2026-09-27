using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public enum AllocationKind
    {
        Value,    // new Type(...)
        Pointer,  // new* Type(...)
        Managed   // new^ Type(...)
    }

    public class NewExpression : AstNode
    {
        public TypeExpression Type { get; set; }
        public List<AstNode> Arguments { get; }
        public AllocationKind Kind { get; }
        public AstNode? Destination { get; }
        public SourceSpan PlacementKeywordSpan { get; set; }

        public NewExpression(TypeExpression type, List<AstNode> arguments, AllocationKind kind, int line, AstNode? destination = null) : base(line)
        {
            Type = type;
            Arguments = arguments;
            Kind = kind;
            Destination = destination;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
