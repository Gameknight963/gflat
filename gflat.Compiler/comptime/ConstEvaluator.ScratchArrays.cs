using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private ConstValue.Array CreateScratchArray(ArrayTypeExpression array, int line)
    {
        if (array.Size is not { } length)
            throw new ConstEvalException("Compile-time scratch arrays require a fixed size", line);
        TypeExpression elementType = _context.ResolveAlias(array.ElementType);
        return new ConstValue.Array(Enumerable.Range(0, length).Select(_ => elementType is ArrayTypeExpression nested
            ? (ConstValue)CreateScratchArray(nested, line) : new ConstValue.Uninitialized()).ToList());
    }

    private static ConstValue ReadInitializedElement(ConstValue.Array array, int index, int line)
        => array.Elements[index] is ConstValue.Uninitialized
            ? throw new ConstEvalException("Cannot read an uninitialized array element at compile time", line)
            : array.Elements[index];

    private static bool ContainsUninitializedStorage(ConstValue value, HashSet<ConstValue>? visited = null)
    {
        if (value is ConstValue.Uninitialized) return true;
        visited ??= new(ReferenceEqualityComparer.Instance);
        if (!visited.Add(value)) return false;
        return value switch
        {
            ConstValue.Array array => array.Elements.Any(element => ContainsUninitializedStorage(element, visited)),
            ConstValue.Struct aggregate => aggregate.Fields.Values.Any(field => ContainsUninitializedStorage(field, visited)),
            ConstValue.ClassInstance aggregate => aggregate.Fields.Values.Any(field => ContainsUninitializedStorage(field, visited)),
            ConstValue.Pointer pointer => ContainsUninitializedStorage(pointer.Target, visited),
            ConstValue.ArrayPointer pointer => ContainsUninitializedStorage(pointer.Target, visited),
            _ => false
        };
    }
}
