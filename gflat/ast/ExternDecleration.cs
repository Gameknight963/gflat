namespace gflat.ast
{
    public class ExternDeclaration : AstNode
    {
        public string Name { get; }
        public TypeExpression ReturnType { get; }
        public List<Parameter> Parameters { get; }
        public bool IsVariadic { get; }

        public ExternDeclaration(string name, TypeExpression returnType, List<Parameter> parameters, bool isVariadic, List<AttributeNode> attributes, int line) : base(line)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            IsVariadic = isVariadic;
            Attributes = attributes;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
