using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private void ValidateParameterType(TypeExpression type, int line)
    {
        ValidateValueType(type, line);
        type = ResolveAlias(type);
        if (type is ReferenceTypeExpression reference) type = ResolveAlias(reference.Inner);
        if (type is ArrayTypeExpression { Size: null })
            throw new TypeCheckException("Array value parameters require a fixed size; use an explicit pointer for a buffer of unknown length", line);
    }
}
