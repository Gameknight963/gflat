using System.Numerics;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    public sealed record ReinterpretCallInfo(AstNode Operand, TypeExpression Source, TypeExpression Result);
    private readonly Dictionary<CallExpression, ReinterpretCallInfo> reinterpretCalls = new();
    public ReinterpretCallInfo? GetReinterpretCall(CallExpression call) => reinterpretCalls.GetValueOrDefault(call);

    private bool TryCheckReinterpretCall(CallExpression node)
    {
        if (node.Callee is not MemberAccessExpression { Member: "reinterpret" } member) return false;
        if (node.TypeArguments.Count != 1 || node.Arguments.Count != 0)
            throw new TypeCheckException("reinterpret requires '.reinterpret<T>()' with one target type and no arguments", node.Span);
        TypeExpression result = ResolveAlias(node.TypeArguments[0]);
        ValidateTypeUsage(result, node.Line);
        if (result is ReferenceTypeExpression) CheckNullableStorage(member.Object, escape: true);
        else member.Object.Accept(this);
        TypeExpression source = ResolveAlias(GetType(member.Object));
        if (result is ReferenceTypeExpression reference)
        {
            if (IsExpressionImmutable(member.Object)) source = TypeQualifiers.Immutable(source);
            else if (IsExpressionReadOnly(member.Object)) source = TypeQualifiers.ReadOnly(source);
            if (!IsReferenceStorage(member.Object) && GetStaticStorage(member.Object) == null || IsManagedStorage(member.Object) ||
                IsPropertyValueStorage(member.Object) || UnsupportedReinterpretStorage(source) || UnsupportedReinterpretStorage(reference.Inner) ||
                !ReinterpretProtectionPreserved(source, reference.Inner))
                throw new TypeCheckException("A reinterpret reference requires existing raw storage and cannot remove readonly or immutable protection", node.Span);
            reinterpretCalls[node] = new(member.Object, source, result);
            _referenceReads.Add(node);
            RecordType(node, reference.Inner);
            return true;
        }
        if (!IsValidReinterpret(source, result))
            throw new TypeCheckException($"Cannot reinterpret '{DisplayTypeName(source)}' as '{DisplayTypeName(result)}'; reinterpret supports raw pointers, integers converted to pointers, sufficiently wide pointer-to-integer conversions, and storage references", node.Span);
        if (result is PointerTypeExpression { IsNullable: false } or FunctionPointerTypeExpression { IsNullable: false })
        {
            if (_constEvaluator.TryEvaluate(member.Object, out ConstValue? value, out _) && value != null &&
                ConstValue.TryIntegerValue(value, out BigInteger integer) &&
                integer % (BigInteger.One << TargetInfo.Default.PointerBits) == 0)
                throw new TypeCheckException("Cannot reinterpret zero as a non-null pointer", node.Span);
        }
        reinterpretCalls[node] = new(member.Object, source, result);
        RecordType(node, result);
        return true;
    }

    private bool IsValidReinterpret(TypeExpression source, TypeExpression result)
    {
        source = ResolveAlias(source);
        result = ResolveAlias(result);
        // A managed reference value cannot lose its tracking contract. A raw
        // address remains an address even when its pointee contains references.
        if (source is ManagedTypeExpression || result is ManagedTypeExpression ||
            source is ReferenceTypeExpression || result is ReferenceTypeExpression or ArrayTypeExpression)
            return false;
        if (source is ArrayTypeExpression array)
            source = new PointerTypeExpression(array.IsImmutableValue ? TypeQualifiers.Immutable(array.ElementType) :
                array.IsReadOnlyValue ? TypeQualifiers.ReadOnly(array.ElementType) : array.ElementType, false, array.Line);
        bool sourceAddress = IsReinterpretAddress(source), resultAddress = IsReinterpretAddress(result);
        if (sourceAddress || resultAddress)
            return sourceAddress && resultAddress &&
                    (source is not PointerTypeExpression sp || result is not PointerTypeExpression rp || ReinterpretProtectionPreserved(sp.Inner, rp.Inner)) &&
                    (!IsNullable(source) || IsNullable(result)) ||
                sourceAddress && IsReinterpretInteger(result) && GetTypeSize(result) * 8 >= TargetInfo.Default.PointerBits ||
                resultAddress && IsReinterpretInteger(source);
        return IsReinterpretInteger(source) && TypesMatch(source, result) ||
            source is NamedTypeExpression { Name: "null" } && IsReinterpretInteger(result) && GetTypeSize(result) * 8 >= TargetInfo.Default.PointerBits;
    }

    private bool IsReinterpretInteger(TypeExpression type) => IsInteger(ReinterpretScalarType(type)) ||
        ResolveAlias(type) is NamedTypeExpression { Name: "bool" };

    private bool UnsupportedReinterpretStorage(TypeExpression type) => ResolveAlias(type) switch
    {
        ManagedTypeExpression => true,
        FunctionPointerTypeExpression function => function.IsManaged || function.IsSignature,
        PointerTypeExpression pointer => !IsReinterpretAddress(pointer),
        _ => false
    };

    private bool ReinterpretProtectionPreserved(TypeExpression source, TypeExpression result)
    {
        source = ResolveAlias(source);
        result = ResolveAlias(result);
        bool primitiveReadOnly = result is NamedTypeExpression named && IsPrimitive(named.Name) && result.IsReadOnlyValue;
        if (source.IsImmutableValue && !result.IsImmutableValue && !primitiveReadOnly || source.IsReadOnlyValue && !result.IsReadOnlyValue) return false;
        if (IsNullable(source) && result is PointerTypeExpression or FunctionPointerTypeExpression && !IsNullable(result)) return false;
        return source is not PointerTypeExpression sourcePointer || result is not PointerTypeExpression resultPointer ||
            ReinterpretProtectionPreserved(sourcePointer.Inner, resultPointer.Inner);
    }

    private bool IsReinterpretAddress(TypeExpression type)
    {
        type = ResolveAlias(type);
        if (type is FunctionPointerTypeExpression function) return !function.IsManaged && !function.IsSignature;
        return type is PointerTypeExpression pointer &&
            !(ResolveAlias(pointer.Inner) is NamedTypeExpression named && IsInterface(named));
    }

    public TypeExpression ReinterpretScalarType(TypeExpression type)
    {
        type = ResolveAlias(type);
        return type is NamedTypeExpression named && ResolveEnum(named) is { } enumeration
            ? ResolveAlias(enumeration.UnderlyingType) : type;
    }

    private void ValidateReinterpretDeclaration(MethodDeclaration method)
    {
        if (ExtensionMemberName(method) == "reinterpret" && (_currentClass != null || _currentStruct != null ||
            method.Parameters.Any(parameter => parameter.IsExtensionReceiver)))
            throw new TypeCheckException("'reinterpret' is a reserved intrinsic member and cannot be declared or overloaded", method.Span);
    }
}
