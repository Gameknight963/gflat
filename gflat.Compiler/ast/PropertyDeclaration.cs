namespace gflat.ast;

/// <summary>A source property. Accessors are lowered to ordinary methods before symbol collection.</summary>
public sealed class PropertyDeclaration : AstNode
{
    public string Name { get; }
    public TypeExpression Type { get; }
    public MethodDeclaration? Getter { get; }
    public MethodDeclaration? Setter { get; }
    public AstNode? Initializer { get; }
    public bool Expanded { get; set; }

    public PropertyDeclaration(string name, TypeExpression type, MethodDeclaration? getter,
        MethodDeclaration? setter, AstNode? initializer, int line) : base(line)
    {
        Name = name;
        Type = type;
        Getter = getter;
        Setter = setter;
        Initializer = initializer;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
