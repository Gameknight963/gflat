namespace gflat.ast;

public sealed class StaticForeachStatement(TypeExpression elementType, string variableName, AstNode collection, BlockStatement body, int line)
    : BlockStatement(new(), line)
{
    public TypeExpression ElementType { get; } = elementType;
    public string VariableName { get; } = variableName;
    public AstNode Collection { get; } = collection;
    public BlockStatement Body { get; } = body;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
