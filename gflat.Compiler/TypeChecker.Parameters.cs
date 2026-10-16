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
        ValidateArrayValueShape(type, line);
    }
}
