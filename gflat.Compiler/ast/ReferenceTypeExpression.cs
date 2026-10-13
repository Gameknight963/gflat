namespace gflat.ast;

// A calling-contract type, restricted to parameters and catch bindings.
public sealed class ReferenceTypeExpression(TypeExpression inner, int line) : TypeExpression(line)
{
    public TypeExpression Inner { get; } = inner;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
