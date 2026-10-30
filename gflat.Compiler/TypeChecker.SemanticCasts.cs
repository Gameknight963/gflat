using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private bool SameRawPointeeShape(TypeExpression left, TypeExpression right)
    {
        left = ResolveAlias(left).WithoutValueProtection();
        right = ResolveAlias(right).WithoutValueProtection();
        if (left is PointerTypeExpression lp && right is PointerTypeExpression rp)
            return lp.IsNullable == rp.IsNullable && SameRawPointeeShape(lp.Inner, rp.Inner);
        if (left is ArrayTypeExpression la && right is ArrayTypeExpression ra)
            return la.Size == ra.Size && SameRawPointeeShape(la.ElementType, ra.ElementType);
        return TypesMatch(left, right);
    }

    private bool IsVoidPointee(TypeExpression type)
        => ResolveAlias(type) is NamedTypeExpression { Name: "void" };

    private bool IsSemanticRawPointerCast(PointerTypeExpression source, PointerTypeExpression result)
    {
        if (SameRawPointeeShape(source.Inner, result.Inner) || IsVoidPointee(source.Inner) || IsVoidPointee(result.Inner)) return true;
        return ResolveAlias(source.Inner) is NamedTypeExpression from && ResolveAlias(result.Inner) is NamedTypeExpression to &&
            _classes.ContainsKey(from.Name) && _classes.ContainsKey(to.Name) &&
            (IsSubclassOf(from.Name, to.Name) || IsSubclassOf(to.Name, from.Name));
    }

    private bool IsSemanticFunctionPointerCast(FunctionPointerTypeExpression source, FunctionPointerTypeExpression result)
        => TypesMatch(new FunctionPointerTypeExpression(source.ReturnType, source.ParameterTypes, source.IsManaged, false, source.Line, source.IsSignature),
            new FunctionPointerTypeExpression(result.ReturnType, result.ParameterTypes, result.IsManaged, false, result.Line, result.IsSignature));

    private bool IsSemanticRawToManagedCast(PointerTypeExpression source, ManagedTypeExpression result)
    {
        if (source.IsReadOnly && !result.IsReadOnly || source.IsImmutable && !result.IsImmutable) return false;
        if (!SameRawPointeeShape(source.Inner, result.Inner) && !IsVoidPointee(source.Inner) && !IsVoidPointee(result.Inner)) return false;
        // Preserve the runtime provider's existing adoption representation boundary.
        // This cast itself does not register, allocate or adopt an object.
        GetCollectorBinding(result);
        return true;
    }

    private bool IsSemanticManagedToRawCast(ManagedTypeExpression source, PointerTypeExpression result)
        => (!source.IsReadOnly || result.IsReadOnly) && (!source.IsImmutable || result.IsImmutable) &&
            (SameRawPointeeShape(source.Inner, result.Inner) || IsVoidPointee(source.Inner) || IsVoidPointee(result.Inner));
}
