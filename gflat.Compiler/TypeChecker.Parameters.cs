using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private void ValidateParameterType(TypeExpression type, int line)
    {
        ValidateTypeUsage(type, line);
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression { Size: null })
            throw new TypeCheckException("Array value parameters require a fixed size; use an explicit pointer for a buffer of unknown length", line);
        if (HasDestructor(type))
            throw new TypeCheckException($"Types with destructors cannot be passed by value ('{TypeName(type)}'); use a pointer", line);
    }
}
