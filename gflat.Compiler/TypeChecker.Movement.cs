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
    private readonly Dictionary<MoveExpression, NewExpression> _moves = new();
    public NewExpression? GetMove(MoveExpression node) => _moves.GetValueOrDefault(node);
    public bool IsFreshValue(AstNode expression) => _transferredPropertyArguments.Contains(expression) || (_propertyReads.ContainsKey(expression) && !IsReferenceRead(expression)) || expression is
        NewExpression or CallExpression or DefaultExpression or ArrayLiteralExpression or MoveExpression;

    public void Visit(MoveExpression node)
    {
        using var context = SourceContext.Enter(node.Span);
        node.Operand.Accept(this);
        TypeExpression type = ResolveAlias(GetType(node.Operand));
        if (node.Operand is not (IdentifierExpression or MemberAccessExpression or IndexExpression or UnaryExpression { Operator: TokenKind.Star }))
            throw new TypeCheckException("move requires writable storage; fresh values already transfer directly", node.Span);
        CheckAssignmentTarget(node.Operand, node.Line);
        if (node.Destination != null)
        {
            node.Destination.Accept(this);
            if (ResolveAlias(GetType(node.Destination)) is not PointerTypeExpression { IsReadOnly: false, IsNullable: false } destination ||
                !TypesMatch(destination.Inner, type))
                throw new TypeCheckException("Move destination must be a non-null pointer to the exact mutable source type", node.Span);
        }
        var constructors = type is NamedTypeExpression n
            ? GetStruct(n.Name)?.Constructors ?? GetClass(n.Name)?.Constructors : null;
        var moves = constructors?.Where(c => c.IsMove).ToArray();
        if (moves?.Length > 1) throw new TypeCheckException("A type can declare only one move constructor", node.Span);
        var constructor = moves?.FirstOrDefault();
        if (constructor == null)
        {
            if (HasDestructor(type))
                throw new TypeCheckException($"Type '{TypeName(type)}' needs a move constructor to transfer its resources", node.Span);
        }
        else
        {
            var address = new UnaryExpression(node.Operand, TokenKind.Ampersand, true, node.Line) { Span = node.Span };
            var creation = new NewExpression(type, new() { address }, node.Destination == null ? AllocationKind.Value : AllocationKind.Pointer, node.Line, node.Destination) { Span = node.Span };
            creation.Accept(this);
            _moves[node] = creation;
        }
        RecordType(node, node.Destination == null ? type : new PointerTypeExpression(type, false, node.Line));
    }
}
