using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class ForeachStatement : AstNode
    {
        public TypeExpression ElementType { get; set; }
        public string VariableName { get; }
        public AstNode Collection { get; }
        public BlockStatement Body { get; }

        public bool IsArrayIteration { get; set; }
        public AstNode? Desugared { get; set; }

        public ForeachStatement(TypeExpression elementType, string variableName, AstNode collection, BlockStatement body, int line) : base(line)
        {
            ElementType = elementType;
            VariableName = variableName;
            Collection = collection;
            Body = body;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
