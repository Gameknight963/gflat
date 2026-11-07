using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private readonly Dictionary<ConstValue.ClassInstance, string> constructionClasses = new(ReferenceEqualityComparer.Instance);

    private static ConstValue? AggregateReceiver(ConstValue? value)
    {
        if (value is ConstValue.BorrowedReference borrowed) value = borrowed.Read();
        if (value is ConstValue.Pointer pointer) value = pointer.Target;
        return value is ConstValue.Struct or ConstValue.ClassInstance ? value : null;
    }

    private MethodDeclaration ConstantDispatchTarget(TypeChecker checker, CallExpression call, MethodDeclaration selected, ConstValue? receiver)
    {
        if (!checker.TryGetVirtualMethodCall(call, out var dispatch)) return selected;
        if (receiver is not ConstValue.ClassInstance instance)
            throw new ConstEvalException("Virtual calls require a constant class receiver", call.Line);
        string activeClass = constructionClasses.GetValueOrDefault(instance) ?? instance.ClassName;
        var info = checker.GetClassByIdentity(activeClass);
        if (info == null || dispatch.SlotIndex < 0 || dispatch.SlotIndex >= info.VirtualMethods.Count)
            throw new ConstEvalException("Cannot resolve the virtual target during constant evaluation", call.Line);
        return info.VirtualMethods[dispatch.SlotIndex];
    }
}
