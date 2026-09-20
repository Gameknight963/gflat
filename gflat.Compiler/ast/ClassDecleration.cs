namespace gflat.ast
{
    public class ClassDeclaration : AstNode
    {
        public string Name { get; internal set; }
        public string SourceName { get; }
        public string? BaseClass { get; }
        public List<string> Interfaces { get; }
        public List<AstNode> Members { get; }
        public TokenKind Accessibility { get; }
        public bool IsAbstract { get; }

        public List<GenericParameter> GenericParameters { get; }
        public bool IsGeneric => GenericParameters.Count > 0;

        public ClassDeclaration(string name, string? baseClass, List<string> interfaces, List<AstNode> members, TokenKind accessibility, bool isAbstract, int line, List<AttributeNode>? attributes = null, List<GenericParameter>? genericParameters = null) : base(line)
        {
            Name = name;
            SourceName = name;
            BaseClass = baseClass;
            Interfaces = interfaces;
            Members = members;
            Accessibility = accessibility;
            IsAbstract = isAbstract;
            Attributes = attributes ?? new List<AttributeNode>();
            GenericParameters = genericParameters ?? new List<GenericParameter>();
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
