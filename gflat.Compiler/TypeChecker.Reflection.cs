using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    public void Visit(TypeofExpression node)
    {
        using var sourceContext = SourceContext.Enter(node.Span);
        RecordType(node, new NamedTypeExpression("std$TypeInfo", null, node.Line));
        _constValues[node] = DescribeType(node.TargetType, node.Line);
    }

    public ConstValue DescribeType(TypeExpression type, int line)
    {
        type = ResolveAlias(type);
        ValidateTypeUsage(type, line);
        return new ConstValue.Struct("std$TypeInfo", new Dictionary<string, ConstValue> {
            ["Name"] = new ConstValue.String(DisplayTypeName(type)),
            ["Size"] = new ConstValue.UInteger((ulong)GetTypeSize(type)),
            ["Alignment"] = new ConstValue.UInteger((ulong)GetTypeAlignment(type)),
            ["IsPointer"] = new ConstValue.Boolean(type is PointerTypeExpression or FunctionPointerTypeExpression),
            ["IsManaged"] = new ConstValue.Boolean(type is ManagedTypeExpression),
            ["IsNullable"] = new ConstValue.Boolean(type is PointerTypeExpression { IsNullable: true } or ManagedTypeExpression { IsNullable: true } or FunctionPointerTypeExpression { IsNullable: true }),
            ["IsReadOnly"] = new ConstValue.Boolean(type.IsReadOnlyValue || type is PointerTypeExpression { IsReadOnly: true } or ManagedTypeExpression { IsReadOnly: true }),
            ["HasDestructor"] = new ConstValue.Boolean(HasDestructor(type))
        });
    }
}
