using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private bool hasNativeOpaqueRecords;

    private NativeRecordLayout? NativeOpaqueLayout(TypeExpression type)
    {
        if (!hasNativeOpaqueRecords) return null;
        type = ResolveAlias(type);
        return type is NamedTypeExpression named
            ? GetStructByIdentity(named.Name)?.Declaration?.NativeOpaqueLayout : null;
    }

    private void RejectNativeOpaqueValue(TypeExpression type, int line)
    {
        if (!hasNativeOpaqueRecords) return;
        type = ResolveAlias(type);
        while (type is ArrayTypeExpression array) type = ResolveAlias(array.ElementType);
        if (NativeOpaqueLayout(type) != null)
            throw new TypeCheckException($"Native type '{DisplayTypeName(type)}' is pointer-only. Construct, copy, and destroy it in native code", line);
    }
}
