using System.Collections.Generic;

namespace gflat.ast
{
    public class OperatorDeclaration : AstNode
    {
        public TokenKind OperatorKind { get; }
        public string OperatorSymbol { get; }
        public TypeExpression ReturnType { get; set; }
        public List<Parameter> Parameters { get; }
        public BlockStatement Body { get; }
        public TokenKind Accessibility { get; }
        public bool IsStatic { get; }

        public OperatorDeclaration(
            TokenKind operatorKind,
            string operatorSymbol,
            TypeExpression returnType,
            List<Parameter> parameters,
            BlockStatement body,
            TokenKind accessibility,
            bool isStatic,
            int line) : base(line)
        {
            OperatorKind = operatorKind;
            OperatorSymbol = operatorSymbol;
            ReturnType = returnType;
            Parameters = parameters;
            Body = body;
            Accessibility = accessibility;
            IsStatic = isStatic;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
