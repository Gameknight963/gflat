using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class CatchClause : AstNode
    {
        public TypeExpression? ExceptionType { get; }
        public string? VariableName { get; }
        public BlockStatement Body { get; }

        public CatchClause(TypeExpression? exceptionType, string? variableName, BlockStatement body, int line) : base(line)
        {
            ExceptionType = exceptionType;
            VariableName = variableName;
            Body = body;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
