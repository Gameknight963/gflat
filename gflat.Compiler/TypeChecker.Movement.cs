using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<AssignmentExpression> _initializations = new();
    public void MarkInitialization(AssignmentExpression node) => _initializations.Add(node);
    public bool IsInitialization(AssignmentExpression node) => _initializations.Contains(node);
    private readonly Dictionary<ConstructorDeclaration, Dictionary<AssignmentExpression, (string Path, bool Live)>> _constructorWrites = new();
    public IReadOnlyDictionary<AssignmentExpression, (string Path, bool Live)> ConstructorWrites(ConstructorDeclaration constructor)
        => _constructorWrites.GetValueOrDefault(constructor) ?? new();
    public void TrackConstructorWrite(ConstructorDeclaration constructor, AssignmentExpression assignment, string path, bool live)
    {
        if (!_constructorWrites.TryGetValue(constructor, out var writes)) _constructorWrites[constructor] = writes = new();
        writes[assignment] = (path, live);
    }

    private readonly HashSet<AstNode> _transferredPropertyArguments = new();
    public bool IsFreshValue(AstNode expression) => _transferredPropertyArguments.Contains(expression) || (_propertyReads.ContainsKey(expression) && !IsReferenceRead(expression)) || expression is
        NewExpression or CallExpression or DefaultExpression or ArrayLiteralExpression or MoveExpression;

    public void Visit(MoveExpression node)
    {
        using var context = SourceContext.Enter(node.Span);
        node.Operand.Accept(this);
        TypeExpression type = ResolveAlias(GetType(node.Operand));
        bool local = node.Operand is IdentifierExpression id && id.Name != "this" &&
            _scopes.Any(scope => scope.ContainsKey(id.Name));
        bool rawStorage = node.Operand is UnaryExpression { Operator: TokenKind.Star, IsPrefix: true } dereference &&
            ResolveAlias(GetType(dereference.Operand)) is PointerTypeExpression;
        if (!local && !rawStorage)
            throw new TypeCheckException("move consumes a whole local or parameter; use an explicit raw-pointer dereference for manual storage relocation", node.Span);
        CheckAssignmentTarget(node.Operand, node.Line);
        if (node.Destination != null)
        {
            node.Destination.Accept(this);
            if (ResolveAlias(GetType(node.Destination)) is not PointerTypeExpression { IsReadOnly: false, IsNullable: false } destination ||
                !TypesMatch(destination.Inner, type))
                throw new TypeCheckException("Move destination must be a non-null pointer to the exact mutable source type", node.Span);
        }
        RecordType(node, node.Destination == null ? type : new PointerTypeExpression(type, false, node.Line));
    }
}
