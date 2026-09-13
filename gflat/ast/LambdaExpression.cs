using System;
using System.Collections.Generic;

namespace gflat.ast
{
    public class LambdaExpression : AstNode
    {
        public bool IsStatic { get; }
        public List<Parameter> Parameters { get; }
        public AstNode Body { get; }
        public bool IsExpressionBody { get; }

        public LambdaExpression(bool isStatic, List<Parameter> parameters, AstNode body, bool isExpressionBody, int line) : base(line)
        {
            IsStatic = isStatic;
            Parameters = parameters;
            Body = body;
            IsExpressionBody = isExpressionBody;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
