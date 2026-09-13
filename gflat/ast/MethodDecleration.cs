using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;

namespace gflat.ast
{
    public class MethodDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression ReturnType { get; }
        public List<Parameter> Parameters { get; }
        public BlockStatement? Body { get; }
        public TokenKind Accessibility { get; }
        public bool IsStatic { get; }
        public bool IsVirtual { get; }
        public bool IsOverride { get; }
        public bool IsAbstract { get; }

        public MethodDeclaration(string name, TypeExpression returnType, List<Parameter> parameters, BlockStatement? body, TokenKind accessibility, bool isStatic, bool isVirtual, bool isOverride, bool isAbstract, int line) : base(line)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            Body = body;
            Accessibility = accessibility;
            IsStatic = isStatic;
            IsVirtual = isVirtual;
            IsOverride = isOverride;
            IsAbstract = isAbstract;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
