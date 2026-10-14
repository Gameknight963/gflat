using gflat.ast;
using gflat.semantics;

namespace gflat;

public partial class TypeChecker
{
    // Facts belong to a binding, not a spelling or its declared storage type.
    private sealed record NullableBinding(Dictionary<string, TypeExpression> Scope, string Name);
    private sealed record NullableCondition(HashSet<NullableBinding> True, HashSet<NullableBinding> False);
    private HashSet<NullableBinding> nullableFacts = new();
    private HashSet<NullableBinding> nullableEscaped = new();
    private Dictionary<AstNode, NullableCondition> nullableConditions = new();
    private Stack<HashSet<NullableBinding>> nullableCleanups = new();
    private HashSet<AstNode> nullableStorageReads = new();
    private HashSet<NullableBinding>? nullableDeferredWrites;

    private sealed class NullableAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private IDisposable IsolateNullableFlow()
    {
        var facts = nullableFacts; var escaped = nullableEscaped; var conditions = nullableConditions;
        var cleanups = nullableCleanups; var storage = nullableStorageReads; var deferred = nullableDeferredWrites;
        nullableFacts = new(); nullableEscaped = new(); nullableConditions = new();
        nullableCleanups = new(); nullableStorageReads = new(); nullableDeferredWrites = null;
        return new NullableAction(() => {
            nullableFacts = facts; nullableEscaped = escaped; nullableConditions = conditions;
            nullableCleanups = cleanups; nullableStorageReads = storage; nullableDeferredWrites = deferred;
        });
    }

    private NullableBinding? NullableLocal(AstNode node)
    {
        if (node is not IdentifierExpression id) return null;
        foreach (var scope in _scopes)
            if (scope.ContainsKey(id.Name)) return new NullableBinding(scope, id.Name);
        return null;
    }

    private TypeExpression NullableReadType(IdentifierExpression node, TypeExpression declared)
    {
        if (nullableFacts.Count == 0 || !IsNullable(ResolveAlias(declared)) || nullableStorageReads.Contains(node) ||
            NullableLocal(node) is not { } binding || !nullableFacts.Contains(binding)) return declared;
        var type = ResolveAlias(declared);
        TypeExpression result = type switch {
            PointerTypeExpression p => new PointerTypeExpression(p.Inner, false, node.Line, p.IsReadOnly),
            ManagedTypeExpression p => new ManagedTypeExpression(p.Inner, false, node.Line, p.IsReadOnly),
            FunctionPointerTypeExpression p => new FunctionPointerTypeExpression(p.ReturnType, p.ParameterTypes, p.IsManaged, false, node.Line),
            _ => declared
        };
        return result.WithReadOnlyValue(type.IsReadOnlyValue);
    }

    private void CheckNullableStorage(AstNode node, bool escape = false)
    {
        // The receiver of p.field still needs its ordinary narrowed read type.
        bool added = node is IdentifierExpression && nullableStorageReads.Add(node);
        try { node.Accept(this); }
        finally { if (added) nullableStorageReads.Remove(node); }
        if (escape && NullableLocal(node) is { } binding)
        {
            nullableEscaped.Add(binding);
            NullableWrite(binding);
        }
    }

    private void NullableWrite(NullableBinding binding)
    {
        nullableFacts.Remove(binding);
        nullableDeferredWrites?.Add(binding);
    }

    private void NullableAssignment(AstNode target, AstNode? value)
    {
        if (NullableLocal(target) is not { } binding) { InvalidateNullableEscapes(); return; }
        // Ref parameters may alias each other (or another exposed slot).
        if (nullableEscaped.Contains(binding)) InvalidateNullableEscapes();
        NullableWrite(binding);
        if (value != null && IsNullable(ResolveAlias(binding.Scope[binding.Name])) &&
            ResolveAlias(GetType(value)) is PointerTypeExpression { IsNullable: false } or
                ManagedTypeExpression { IsNullable: false } or FunctionPointerTypeExpression { IsNullable: false })
            nullableFacts.Add(binding);
    }

    private void NullableParameter(Parameter parameter)
    {
        if (ResolveAlias(parameter.Type) is not ReferenceTypeExpression) return;
        foreach (var scope in _scopes)
            if (scope.ContainsKey(parameter.Name)) { nullableEscaped.Add(new NullableBinding(scope, parameter.Name)); break; }
    }

    private void InvalidateNullableEscapes()
    {
        foreach (var binding in nullableEscaped) NullableWrite(binding);
    }

    private IDisposable NullableCall() => new NullableAction(InvalidateNullableEscapes);

    private IDisposable NullableAssignmentResult(AssignmentExpression node) => new NullableAction(() => {
        NullableAssignment(node.Target, node.Operator == TokenKind.Equals ? node.Value : null);
        if (_compoundOperatorTargets.ContainsKey(node) || CopiesValue(node.Value, GetType(node.Target)) || HasDestructor(GetType(node.Target)))
            InvalidateNullableEscapes();
    });

    private void CheckNullableArgument(AstNode argument)
    {
        argument.Accept(this);
        // Copies are emitted while evaluating arguments, before later arguments.
        // Parameter selection follows expression checking, so conservatively
        // account for any value that may require a user copy constructor.
        if (!IsFreshValue(argument) && RequiresCopy(GetType(argument))) InvalidateNullableEscapes();
    }

    private IDisposable NullableCastResult(CastExpression node) => new NullableAction(() => {
        if (CopiesValue(node.Operand, GetType(node))) InvalidateNullableEscapes();
    });

    private static HashSet<NullableBinding> IntersectNullable(IEnumerable<HashSet<NullableBinding>> paths)
    {
        HashSet<NullableBinding>? result = null;
        foreach (var path in paths) { if (result == null) result = new(path); else result.IntersectWith(path); }
        return result ?? new();
    }

    private NullableCondition NullableTruth(AstNode expression) => nullableConditions.TryGetValue(expression, out var condition)
        ? condition : new(new(nullableFacts), new(nullableFacts));

    private bool NullableFallsThrough(AstNode statement) => new ControlFlowGraph(statement, expression =>
        _constEvaluator.TryEvaluate(expression, out var value, out _) && value is gflat.comptime.ConstValue.Boolean boolean
            ? boolean.Value : null).FallsThrough;

    private void NullableBinaryOperands(BinaryExpression node)
    {
        bool nullComparison = node.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals &&
            (node.Left is LiteralExpression { Token.Kind: TokenKind.Null } || node.Right is LiteralExpression { Token.Kind: TokenKind.Null });
        if (nullComparison) CheckNullableStorage(node.Left); else node.Left.Accept(this);
        bool logical = node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe && TypesMatch(GetType(node.Left), Bool);
        if (!logical)
        {
            if (nullComparison) CheckNullableStorage(node.Right); else node.Right.Accept(this);
            return;
        }
        var left = NullableTruth(node.Left);
        nullableFacts = new(node.Operator == TokenKind.AmpersandAmpersand ? left.True : left.False);
        if (nullComparison) CheckNullableStorage(node.Right); else node.Right.Accept(this);
        if (logical)
        {
            var right = NullableTruth(node.Right);
            nullableConditions[node] = node.Operator == TokenKind.AmpersandAmpersand
                ? new(new(right.True), IntersectNullable([left.False, right.False]))
                : new(IntersectNullable([left.True, right.True]), new(right.False));
            nullableFacts = IntersectNullable([nullableConditions[node].True, nullableConditions[node].False]);
        }
    }

    private IDisposable NullableBinaryResult(BinaryExpression node) => new NullableAction(() => {
        if (_operatorTargets.ContainsKey(node)) { InvalidateNullableEscapes(); nullableConditions.Remove(node); return; }
        if (node.Operator is not (TokenKind.EqualsEquals or TokenKind.NotEquals) || !TypesMatch(GetType(node), Bool)) return;
        AstNode? checkedValue = node.Left is LiteralExpression { Token.Kind: TokenKind.Null } ? node.Right :
            node.Right is LiteralExpression { Token.Kind: TokenKind.Null } ? node.Left : null;
        if (checkedValue == null || NullableLocal(checkedValue) is not { } binding || !IsNullable(ResolveAlias(binding.Scope[binding.Name]))) return;
        var nonNull = new HashSet<NullableBinding>(nullableFacts) { binding };
        var maybeNull = new HashSet<NullableBinding>(nullableFacts); maybeNull.Remove(binding);
        nullableConditions[node] = node.Operator == TokenKind.NotEquals ? new(nonNull, maybeNull) : new(maybeNull, nonNull);
    });

    private IDisposable NullableUnaryResult(UnaryExpression node) => new NullableAction(() => {
        if (_unaryOperatorTargets.ContainsKey(node)) { InvalidateNullableEscapes(); return; }
        if (node.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            if (NullableLocal(node.Operand) is { } binding) NullableWrite(binding);
            else InvalidateNullableEscapes();
        }
        if (node.Operator == TokenKind.Bang && node.IsPrefix && TypesMatch(GetType(node.Operand), Bool))
        {
            var operand = NullableTruth(node.Operand);
            nullableConditions[node] = new(new(operand.False), new(operand.True));
        }
    });

    private void NullableBranch(IfStatement node)
    {
        var condition = NullableTruth(node.Condition);
        nullableFacts = new(condition.True); node.Then.Accept(this);
        var paths = new List<HashSet<NullableBinding>>();
        if (NullableFallsThrough(node.Then)) paths.Add(new(nullableFacts));
        nullableFacts = new(condition.False); node.Else?.Accept(this);
        if (node.Else == null || NullableFallsThrough(node.Else)) paths.Add(new(nullableFacts));
        nullableFacts = IntersectNullable(paths);
    }

    private IDisposable NullableBlock()
    {
        var writes = new HashSet<NullableBinding>(); nullableCleanups.Push(writes);
        return new NullableAction(() => { nullableCleanups.Pop(); foreach (var binding in writes) NullableWrite(binding); });
    }

    private void CheckNullableDefer(AstNode body)
    {
        var previous = nullableFacts; var previousWrites = nullableDeferredWrites;
        var writes = new HashSet<NullableBinding>(); nullableFacts = new(); nullableDeferredWrites = writes;
        try { body.Accept(this); }
        finally {
            nullableFacts = previous; nullableDeferredWrites = previousWrites;
            foreach (var binding in writes) { NullableWrite(binding); if (nullableCleanups.Count != 0) nullableCleanups.Peek().Add(binding); }
        }
    }

    private void NullableLoopEntry() => nullableFacts.Clear();
    private void NullableLoopBody(AstNode? condition)
        => nullableFacts = condition == null ? new() : new(NullableTruth(condition).True);

    private void NullableScopeExit(Dictionary<string, TypeExpression> scope)
    {
        if (nullableEscaped.Count != 0 && scope.Values.Any(HasDestructor)) InvalidateNullableEscapes();
        nullableFacts.RemoveWhere(binding => ReferenceEquals(binding.Scope, scope));
        nullableEscaped.RemoveWhere(binding => ReferenceEquals(binding.Scope, scope));
    }
}
