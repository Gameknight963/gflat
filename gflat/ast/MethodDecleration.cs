using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;

namespace gflat.ast
{
    public class MethodDeclaration : AstNode
    {
        public string Name { get; }
        public string? StringLiteralPrefix { get; set; }
        public TypeExpression ReturnType { get; set; }
        public List<Parameter> Parameters { get; }
        public BlockStatement? Body { get; }
        public TokenKind Accessibility { get; }
        public bool IsStatic { get; }
        public bool IsVirtual { get; }
        public bool IsOverride { get; }
        public bool IsAbstract { get; }
        public bool IsReadOnly { get; }
        public bool IsConst { get; }

        public List<GenericParameter> GenericParameters { get; }
        public bool IsGeneric => GenericParameters.Count > 0;
        public bool Throws { get; set; }

        public MethodDeclaration(string name, TypeExpression returnType, List<Parameter> parameters, BlockStatement? body, TokenKind accessibility, bool isStatic, bool isVirtual, bool isOverride, bool isAbstract, int line, bool isReadOnly = false, bool isConst = false, List<AttributeNode>? attributes = null, List<GenericParameter>? genericParameters = null, bool throws = false) : base(line)
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
            IsReadOnly = isReadOnly;
            IsConst = isConst;
            Attributes = attributes ?? new List<AttributeNode>();
            GenericParameters = genericParameters ?? new List<GenericParameter>();
            Throws = throws;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
