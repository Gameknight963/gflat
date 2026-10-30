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
        if (member.IsArrow || node.TypeArguments.Count != 1 || node.Arguments.Count != 0)
            throw new TypeCheckException("reinterpret requires '.reinterpret<T>()' with one target type and no arguments", node.Span);
        member.Object.Accept(this);
        TypeExpression source = ResolveAlias(GetType(member.Object));
        TypeExpression result = ResolveAlias(node.TypeArguments[0]);
        ValidateTypeUsage(result, node.Line);
        if (!IsValidReinterpret(source, result))
            throw new TypeCheckException($"Cannot reinterpret '{DisplayTypeName(source)}' as '{DisplayTypeName(result)}'; use equal-sized numeric scalars or raw addresses and pointer-sized integers", node.Span);
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
            source is ReferenceTypeExpression or ArrayTypeExpression || result is ReferenceTypeExpression or ArrayTypeExpression)
            return false;
        bool sourceAddress = IsReinterpretAddress(source), resultAddress = IsReinterpretAddress(result);
        if (sourceAddress || resultAddress)
            return sourceAddress && resultAddress || sourceAddress && IsInteger(result) &&
                GetTypeSize(result) * 8 == TargetInfo.Default.PointerBits ||
                resultAddress && IsInteger(source) && GetTypeSize(source) * 8 == TargetInfo.Default.PointerBits;
        TypeExpression sourceScalar = ReinterpretScalarType(source), resultScalar = ReinterpretScalarType(result);
        return IsNumeric(sourceScalar) && IsNumeric(resultScalar) && GetTypeSize(sourceScalar) == GetTypeSize(resultScalar);
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
