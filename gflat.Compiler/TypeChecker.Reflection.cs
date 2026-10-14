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
        return DescribeResolvedType(type, line);
    }

    // Only the queried type needs value-usage validation. A pointer can describe
    // an interface/void element although neither is an ordinary value object.
    private ConstValue DescribeResolvedType(TypeExpression type, int line)
    {
        type = ResolveAlias(type);
        bool hasLayout = type is not NamedTypeExpression named ||
            named.Name != "void" && !IsInterface(named);
        bool isReference = type is ReferenceTypeExpression;
        int kind = type switch {
            PointerTypeExpression => 5,
            ManagedTypeExpression => 6,
            ArrayTypeExpression => 7,
            FunctionPointerTypeExpression => 8,
            ReferenceTypeExpression => 9,
            NamedTypeExpression n when IsInterface(n) => 4,
            NamedTypeExpression n when ResolveEnum(n) != null => 3,
            NamedTypeExpression n when IsClass(n.Name) => 1,
            NamedTypeExpression n when GetStruct(n.Name) != null => 2,
            _ => 0
        };
        TypeExpression? element = type switch {
            PointerTypeExpression pointer => pointer.Inner,
            ManagedTypeExpression managed => managed.Inner,
            ArrayTypeExpression array => array.ElementType,
            ReferenceTypeExpression reference => reference.Inner,
            _ => null
        };
        return new ConstValue.Struct("std$TypeInfo", new Dictionary<string, ConstValue> {
            ["Name"] = new ConstValue.String(DisplayTypeName(type)),
            ["Kind"] = new ConstValue.Integer(kind),
            ["HasLayout"] = new ConstValue.Boolean(hasLayout),
            ["Size"] = new ConstValue.UInteger((ulong)(!hasLayout ? 0 : isReference ? TargetInfo.Default.PointerBytes : GetTypeSize(type))),
            ["Alignment"] = new ConstValue.UInteger((ulong)(!hasLayout ? 0 : isReference ? TargetInfo.Default.PointerBytes : GetTypeAlignment(type))),
            ["IsPointer"] = new ConstValue.Boolean(type is PointerTypeExpression or FunctionPointerTypeExpression),
            ["IsManaged"] = new ConstValue.Boolean(type is ManagedTypeExpression),
            ["IsNullable"] = new ConstValue.Boolean(type is PointerTypeExpression { IsNullable: true } or ManagedTypeExpression { IsNullable: true } or FunctionPointerTypeExpression { IsNullable: true }),
            ["IsReadOnly"] = new ConstValue.Boolean(type.IsReadOnlyValue),
            ["HasDestructor"] = new ConstValue.Boolean(hasLayout && !isReference && HasDestructor(type)),
            ["ElementType"] = element == null ? new ConstValue.Integer(0) :
                new ConstValue.Pointer("std$TypeInfo", DescribeResolvedType(element, line)),
            ["HasFixedLength"] = new ConstValue.Boolean(type is ArrayTypeExpression { Size: not null }),
            ["Length"] = new ConstValue.UInteger((ulong)(type is ArrayTypeExpression { Size: int length } ? length : 0))
        });
    }
}
