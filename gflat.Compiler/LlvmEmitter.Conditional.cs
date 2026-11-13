using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    public void Visit(ConditionalExpression node) => EmitExpressionWithCleanup(() =>
    {
        node.Condition.Accept(this);
        string condition = Pop();
        TypeExpression type = _typeChecker.GetType(node);
        string llvmType = EmitType(type);
        string whenTrue = NewLabel("conditional_true"), whenFalse = NewLabel("conditional_false"), done = NewLabel("conditional_done");
        string? slot = llvmType == "void" ? null : NewTemp();
        if (slot != null) EmitAlloca(llvmType, slot);
        Emit($"    br i1 {condition}, label %{whenTrue}, label %{whenFalse}");
        EmitBranch(whenTrue, node.WhenTrue);
        EmitBranch(whenFalse, node.WhenFalse);
        Emit($"{done}:");
        if (slot != null)
        {
            string result = NewTemp();
            Emit($"    {result} = load {llvmType}, {llvmType}* {slot}");
            RememberOwnedLocation(type, result, slot);
            PushManaged(node, result);
        }

        void EmitBranch(string label, AstNode branch)
        {
            Emit($"{label}:");
            EmitValueForTarget(branch, type);
            if (slot != null)
            {
                string value = EmitImplicitCast(Pop(), _typeChecker.GetType(branch), type, branch);
                if (_typeChecker.RequiresRelocation(type)) EmitManagedStore(type, value, slot, initialize: true);
                else Emit($"    store {llvmType} {value}, {llvmType}* {slot}");
            }
            Emit($"    br label %{done}");
        }
    });
}
