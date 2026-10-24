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
        // Primitive keywords cannot name lexical aliases. Their qualifiers are
        // already part of the source type, and resolving them cannot specialize.
        if (source is NamedTypeExpression { Namespace: null, TypeArguments.Count: 0 } primitive && IsPrimitive(primitive.Name))
        {
            forward?.Invoke(source);
            return source;
        }
        return ResolveAliasScoped(source, forward);
    }

    // Keep the captured publication delegate out of the common primitive path.
    private TypeExpression ResolveAliasScoped(TypeExpression source, Action<TypeExpression>? forward)
    {
        if (activeTypeBindings.TryGetValue(source, out var active))
        {
            if (active.Identity == null) throw new TypeCheckException("Cyclic type alias", source.Span);
            CheckPublishedTypeBinding(active.Identity, source.Line);
            forward?.Invoke(active.Identity);
            return active.Identity;
        }
        var binding = new ActiveTypeBinding(forward);
        sourceTypeBindings.Remove(source);
        activeTypeBindings.Add(source, binding);
        try
        {
            var resolved = ResolveAliasCore(source, Publish);
            Publish(resolved);
            RecordSourceTypeBinding(source, binding.Identity!);
            return binding.Identity!;
        }
        finally { activeTypeBindings.Remove(source); }

        void Publish(TypeExpression identity)
        {
            identity = MarkResolvedNominal(identity);
            CheckPublishedTypeBinding(identity, source.Line);
            TypeExpression qualified = CanonicalizeBoundScalarProtection(identity.WithQualifiersFrom(source));
            if (source.InterfaceConstraints.Count > 0) qualified = qualified.WithInterfaceConstraints(source.InterfaceConstraints);
            if (source.GenericUse != null) qualified = qualified.WithGenericUse(source.GenericUse);
            if ((source.GenericParameterView ?? identity.GenericParameterView) is { } view) qualified = qualified.WithGenericParameterView(view);
            if (aliasContracts.TryGetValue(identity, out var contract)) aliasContracts[qualified] = contract;
            binding.Publish(qualified);
        }
    }

    // Enums, like built-in scalar types, have no reachable mutable storage.
    // Do this after binding so aliases and generic substitutions use enum identity;
    // the syntax-level qualifier helper need not know about the symbol table.
    private TypeExpression CanonicalizeBoundScalarProtection(TypeExpression type) =>
        type is NamedTypeExpression { IsImmutableValue: true } named && ResolveEnum(named) != null
            ? type.WithoutValueProtection().WithReadOnlyValue(true) : type;

    private TypeExpression MarkResolvedNominal(TypeExpression type)
    {
        if (type is not NamedTypeExpression { Namespace: null, TypeArguments.Count: 0, IsResolvedNominal: false } named ||
            !_classes.ContainsKey(named.Name) && !_structs.ContainsKey(named.Name) &&
            !_interfaces.ContainsKey(named.Name) && !_enums.ContainsKey(named.Name)) return type;
        var resolved = named.AsResolvedNominal();
        if (aliasContracts.TryGetValue(named, out var contract)) aliasContracts[resolved] = contract;
        return resolved;
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

    // A parent's registration includes its concrete nested declarations before
    // its bodies are checked. Forward only those existing identities: resolving
    // an alias or creating a nested specialization here would reenter checking
    // before the enclosing binding has published its own identity.
    private NamedTypeExpression? TryProjectRegisteredNestedType(NestedTypeExpression source, TypeExpression parent)
    {
        if (source.TypeArguments.Count != 0 || parent is not NamedTypeExpression named) return null;
        string owner = named.Namespace == null ? named.Name : named.Namespace.Replace("::", "$") + "$" + named.Name;
        string identity = owner + "." + source.Member;
        if (!_classes.ContainsKey(identity) && !_structs.ContainsKey(identity) &&
            !_interfaces.ContainsKey(identity) && !_enums.ContainsKey(identity)) return null;
        var resolved = NamedTypeExpression.Resolved(identity, source.Line);
        resolved.Span = source.Span;
        return resolved;
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
