using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    public void Visit(TypeofExpression node)
    {
        using var sourceContext = SourceContext.Enter(node.Span);
        using var unevaluated = UnevaluatedOperand();
        node.Operand.Accept(this);
        RecordType(node, ReflectionType(node.Line));
        _constValues[node] = ReflectExpressionType(node.Operand, node.Line);
    }

    public ConstValue.Reflection ReflectExpressionType(AstNode operand, int line)
    {
        // Constant storage is protected implicitly. The query reports the
        // declared type, retaining any qualifiers explicitly present there.
        var type = GetConstantDeclaration(operand) switch {
            VariableDeclaration variable => variable.Type,
            FieldDeclaration field => field.Type,
            _ => GetType(operand)
        };
        return ReflectType(type, line);
    }
}
