using gflat.comptime;

namespace gflat.ast;

// A value substituted by compile-time expansion, retaining its checked type.
public sealed class ConstantExpression(ConstValue value, TypeExpression type, int line) : AstNode(line)
{
    public ConstValue Value { get; } = value;
    public TypeExpression Type { get; } = type;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
