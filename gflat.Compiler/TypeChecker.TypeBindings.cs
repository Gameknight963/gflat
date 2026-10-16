using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private sealed class ActiveTypeBinding(Action<TypeExpression>? forward)
    {
        public TypeExpression? Identity { get; private set; }
        public void Publish(TypeExpression identity) { Identity = identity; forward?.Invoke(identity); }
    }
    private readonly Dictionary<TypeExpression, ActiveTypeBinding> activeTypeBindings = new(ReferenceEqualityComparer.Instance);

    private TypeExpression ResolveAlias(TypeExpression source, Action<TypeExpression>? forward)
    {
        if (activeTypeBindings.TryGetValue(source, out var active))
        {
            if (active.Identity == null) throw new TypeCheckException("Cyclic type alias", source.Span);
            CheckPublishedTypeBinding(active.Identity, source.Line);
            forward?.Invoke(active.Identity);
            return active.Identity;
        }
        var binding = new ActiveTypeBinding(forward);
        activeTypeBindings.Add(source, binding);
        try
        {
            var resolved = ResolveAliasCore(source, Publish);
            Publish(resolved);
            return binding.Identity!;
        }
        finally { activeTypeBindings.Remove(source); }

        void Publish(TypeExpression identity)
        {
            CheckPublishedTypeBinding(identity, source.Line);
            TypeExpression qualified = source.IsReadOnlyValue ? TypeQualifiers.ReadOnly(identity) : identity;
            if (aliasContracts.TryGetValue(identity, out var contract)) aliasContracts[qualified] = contract;
            binding.Publish(qualified);
        }
    }

    // Publication carries registered identities only. It never evaluates bounds,
    // creates another specialization, or marks an unchecked body as checked.
    private void CheckPublishedTypeBinding(TypeExpression type, int line)
    {
        switch (type)
        {
            case NamedTypeExpression named:
                CheckFailedTypeSpecialization(named.Name, line);
                CheckInvalidInlineLayout(named.Name);
                break;
            case PointerTypeExpression pointer: CheckPublishedTypeBinding(pointer.Inner, line); break;
            case ManagedTypeExpression managed:
                CheckPublishedTypeBinding(managed.Inner, line);
                if (managed.Collector != null) CheckPublishedTypeBinding(managed.Collector, line);
                break;
            case ReferenceTypeExpression reference: CheckPublishedTypeBinding(reference.Inner, line); break;
            case ArrayTypeExpression array: CheckPublishedTypeBinding(array.ElementType, line); break;
            case FunctionPointerTypeExpression function:
                CheckPublishedTypeBinding(function.ReturnType, line);
                foreach (var parameter in function.ParameterTypes) CheckPublishedTypeBinding(parameter, line);
                break;
        }
    }

    private int? ResolveArrayLength(ArrayTypeExpression array)
    {
        if (array.Size.HasValue || array.SizeExpression == null) return array.Size;
        if (!_types.ContainsKey(array.SizeExpression)) array.SizeExpression.Accept(this);
        if (!_constEvaluator.TryEvaluate(array.SizeExpression, out ConstValue? value, out string? error))
            throw new TypeCheckException($"Array size must be a compile-time constant: {error}", array.Line);
        if (!ConstValue.TryIntegerValue(value!, out var bound))
            throw new TypeCheckException($"Array size expression must evaluate to integer, got {value}", array.Line);
        if (bound <= 0 || bound > int.MaxValue)
            throw new TypeCheckException("Array size must be between 1 and 2147483647", array.Line);
        return (int)bound;
    }
}
