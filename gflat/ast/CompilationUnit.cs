namespace gflat.ast
{
    public class CompilationUnit : AstNode
    {
        public List<UsingDirective> Usings { get; }
        public List<NamespaceDeclaration> Namespaces { get; }
        public List<AstNode> Members { get; }

        public CompilationUnit(List<UsingDirective> usings, List<NamespaceDeclaration> namespaces, List<AstNode> members, int line) : base(line)
        {
            Usings = usings;
            Namespaces = namespaces;
            Members = members;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
