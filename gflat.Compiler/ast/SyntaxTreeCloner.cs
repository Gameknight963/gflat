using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace gflat.ast;

// Copies pristine parser output without applying specialization or binding.
// Unlike AstCloner, this preserves every syntax field and graph relationship.
internal sealed class SyntaxTreeCloner(CancellationToken cancellationToken, int capacity = 0)
{
    private static readonly Func<object, object> ShallowCopy =
        (Func<object, object>)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<object, object>));
    private static readonly ConcurrentDictionary<Type, Action<object, object, SyntaxTreeCloner>> Copiers = new();
    private static readonly ConcurrentDictionary<Type, Func<int, IList>> ListConstructors = new();
    private static readonly MethodInfo CreateListMethod = typeof(SyntaxTreeCloner)
        .GetMethod(nameof(CreateList), BindingFlags.NonPublic | BindingFlags.Static)!;
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
            var constructor = ListConstructors.GetOrAdd(type, static type =>
            {
                // List<T> can allocate its final storage once instead of growing while copied.
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                    return CreateListMethod.MakeGenericMethod(type.GetGenericArguments())
                        .CreateDelegate<Func<int, IList>>();
                var info = type.GetConstructor(Type.EmptyTypes)
                    ?? throw new NotSupportedException("Syntax list constructor");
                var invoker = ConstructorInvoker.Create(info);
                return _ => (IList)invoker.Invoke();
            });
            var clone = constructor(list.Count);
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
        Copiers.GetOrAdd(type, CreateCopier)(value, result, this);
        return result;
    }

    private static IList CreateList<T>(int capacity) => new List<T>(capacity);

    private static bool IsImmutable(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner) return IsImmutable(inner);
        return type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string) ||
            type == typeof(SourceSpan) || type == typeof(SourceId) || type == typeof(SourceFile) || type == typeof(Token);
    }

    private static Action<object, object, SyntaxTreeCloner> CreateCopier(Type type)
    {
        var fields = MutableFields(type);
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return (source, target, cloner) =>
            {
                foreach (var field in fields)
                    if (field.GetValue(source) is { } child) field.SetValue(target, cloner.Copy(child));
            };

        // Compile the field traversal once per syntax shape. Copy still owns graph
        // identity, cancellation and collection handling on every recursive edge.
        var method = new DynamicMethod("Copy_" + type.Name, typeof(void),
            [typeof(object), typeof(object), typeof(SyntaxTreeCloner)], typeof(SyntaxTreeCloner), true);
        var il = method.GetILGenerator();
        var child = il.DeclareLocal(typeof(object));
        var copy = typeof(SyntaxTreeCloner).GetMethod(nameof(Copy), BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var field in fields)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType!);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Stloc, child);
            il.Emit(OpCodes.Ldloc, child);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, field.DeclaringType!);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, child);
            il.Emit(OpCodes.Call, copy);
            il.Emit(OpCodes.Castclass, field.FieldType);
            il.Emit(OpCodes.Stfld, field);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<object, object, SyntaxTreeCloner>>();
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
