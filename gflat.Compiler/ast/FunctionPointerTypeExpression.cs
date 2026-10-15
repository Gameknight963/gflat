using System;
using System.Collections.Generic;

namespace gflat.ast
{
    public class FunctionPointerTypeExpression : TypeExpression
    {
        public TypeExpression ReturnType { get; }
        public List<TypeExpression> ParameterTypes { get; }
        public bool IsManaged { get; }
        public bool IsSignature { get; }
        public bool IsNullable { get; }

        public FunctionPointerTypeExpression(
            TypeExpression returnType,
            List<TypeExpression> parameterTypes,
            bool isManaged,
            bool isNullable,
            int line, bool isSignature = false) : base(line)
        {
            IsSignature = isSignature;
            ReturnType = returnType;
            ParameterTypes = parameterTypes;
            IsManaged = isManaged;
            IsNullable = isNullable;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
