using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class LlvmEmitter
{
    private bool TryEmitEmptyArrayIteration(ForeachStatement statement, ArrayTypeExpression array)
    {
        if (array.Size != 0) return false;
        EmitArgument(statement.Collection);
        _ = Pop();
        return true;
    }

    private bool TryEmitConstantArrayValue(AstNode expression, TypeExpression target)
    {
        if (_typeChecker.ResolveAlias(target) is not ArrayTypeExpression { Size: not null } ||
            !_typeChecker.TryGetConstValue(expression, out var value) || value is not ConstValue.Array)
            return false;

        string address = GetOrCreateConstantStorage(_typeChecker.GetConstantDeclaration(expression) ?? expression, value, target);
        EmitConstantArrayAggregate((ConstValue.Array)value, target, address);
        return true;
    }

    private void EmitConstantArrayAggregate(ConstValue.Array value, TypeExpression target, string? address = null)
    {
        if (address == null)
        {
            address = NewGlobal();
            EmitGlobal($"{address} = private constant {EmitConstInitializer(value, target)}");
        }
        string type = EmitType(target), aggregate = NewTemp();
        Emit($"    {aggregate} = load {type}, {type}* {address}");
        ProtectManagedValue(target, aggregate);
        Push(aggregate);
    }
}
