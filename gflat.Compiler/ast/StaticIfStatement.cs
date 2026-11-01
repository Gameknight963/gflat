namespace gflat.ast;

public sealed class StaticIfStatement(AstNode condition, BlockStatement thenBody, AstNode? elseBody, int line)
    : BlockStatement(new(), line)
{
    public AstNode Condition { get; } = condition;
    public BlockStatement ThenBody { get; } = thenBody;
    public AstNode? ElseBody { get; } = elseBody;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
