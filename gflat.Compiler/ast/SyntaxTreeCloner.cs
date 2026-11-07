using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace gflat.ast;

// Copies pristine parser output without applying specialization or binding.
// Unlike AstCloner, this preserves every syntax field and graph relationship.
internal sealed class SyntaxTreeCloner(CancellationToken cancellationToken, int capacity = 0)
{
    private static readonly Func<object, object> ShallowCopy =
        (Func<object, object>)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<object, object>));
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new();
    private readonly Dictionary<object, object> copies = new(capacity, ReferenceEqualityComparer.Instance);
    private int copied;
    internal int ObjectCount => copies.Count;

    internal CompilationUnit Clone(CompilationUnit tree)
    {
        using var timing = PipelineTimings.Measure("syntax.clone");
        cancellationToken.ThrowIfCancellationRequested();
        var result = (CompilationUnit)Copy(tree);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private object Copy(object value)
    {
        if ((++copied & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
        Type type = value.GetType();
        if (IsImmutable(type)) return value;
        if (copies.TryGetValue(value, out var previous)) return previous;
        if (value is Array array)
        {
            if (array.Rank != 1) throw new NotSupportedException("Multidimensional syntax storage");
            if (array.Length == 0) return value;
            var clone = (Array)array.Clone();
            copies[value] = clone;
            for (int index = 0; index < array.Length; index++)
                if (array.GetValue(index) is { } item) clone.SetValue(Copy(item), index);
            return clone;
        }
        if (value is IList list)
        {
            var constructor = type.GetConstructor(Type.EmptyTypes) ?? throw new NotSupportedException("Syntax list constructor");
            var clone = (IList)constructor.Invoke(null);
            copies[value] = clone;
            for (int index = 0; index < list.Count; index++)
            {
                var item = list[index];
                clone.Add(item == null ? null : Copy(item));
            }
            return clone;
        }
        if (value is IDictionary dictionary)
        {
            var comparer = type.GetProperty("Comparer") ?? throw new NotSupportedException("Syntax dictionary comparer");
            var constructor = type.GetConstructor([comparer.PropertyType]) ?? throw new NotSupportedException("Syntax dictionary constructor");
            var clone = (IDictionary)constructor.Invoke([comparer.GetValue(value)]);
            copies[value] = clone;
            foreach (DictionaryEntry item in dictionary)
                clone.Add(Copy(item.Key), item.Value == null ? null : Copy(item.Value));
            return clone;
        }
        if (type.IsValueType || type.Namespace != typeof(AstNode).Namespace)
            throw new NotSupportedException("Unsupported mutable syntax storage: " + type);
        object result = ShallowCopy(value);
        copies[value] = result;
        foreach (var field in Fields.GetOrAdd(type, MutableFields))
            if (field.GetValue(value) is { } child) field.SetValue(result, Copy(child));
        return result;
    }

    private static bool IsImmutable(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner) return IsImmutable(inner);
        return type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string) ||
            type == typeof(SourceSpan) || type == typeof(SourceId) || type == typeof(SourceFile) || type == typeof(Token);
    }

    private static FieldInfo[] MutableFields(Type type)
    {
        var result = new List<FieldInfo>();
        for (Type? owner = type; owner != null && owner != typeof(object); owner = owner.BaseType)
            foreach (var field in owner.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!IsImmutable(field.FieldType))
                {
                    if (field.FieldType.IsValueType) throw new NotSupportedException("Unsupported syntax value: " + field.FieldType);
                    result.Add(field);
                }
        return result.ToArray();
    }
}
