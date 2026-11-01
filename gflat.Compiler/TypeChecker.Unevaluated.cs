using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private readonly Stack<AstNode?> unevaluatedFunctions = new();
    private bool InUnevaluatedOperand => unevaluatedFunctions.Contains(_currentFunction);

    private IDisposable UnevaluatedOperand()
    {
        unevaluatedFunctions.Push(_currentFunction);
        var facts = nullableFacts;
        var escaped = nullableEscaped;
        var conditions = nullableConditions;
        var storage = nullableStorageReads;
        var deferred = nullableDeferredWrites;
        nullableFacts = new(facts);
        nullableEscaped = new(escaped);
        nullableConditions = new(conditions);
        nullableStorageReads = new(storage);
        nullableDeferredWrites = deferred == null ? null : new(deferred);
        return new NullableAction(() => {
            unevaluatedFunctions.Pop();
            nullableFacts = facts;
            nullableEscaped = escaped;
            nullableConditions = conditions;
            nullableStorageReads = storage;
            nullableDeferredWrites = deferred;
        });
    }
}
