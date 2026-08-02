namespace gflat.ast
{
    public class CompilationUnit : AstNode
    {
        public List<UsingDirective> Usings { get; }
        public List<NamespaceDeclaration> Namespaces { get; }

        public CompilationUnit(List<UsingDirective> usings, List<NamespaceDeclaration> namespaces, int line) : base(line)
        {
            Usings = usings;
            Namespaces = namespaces;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
