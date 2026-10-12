using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly Stack<List<IDeferAction>> _argumentOwners = new();
    private readonly List<IDeferAction> _parameterOwners = new();
    private readonly Dictionary<AssignmentExpression, string> _constructorLiveFlags = new();

    private void PrepareConstructorWrites(ConstructorDeclaration constructor)
    {
        _constructorLiveFlags.Clear();
        foreach (var group in _typeChecker.ConstructorWrites(constructor).GroupBy(w => w.Value.Path))
        {
            string flag = NewTemp();
            Emit($"    {flag} = alloca i1");
            Emit($"    store i1 {(group.First().Value.Live ? "true" : "false")}, i1* {flag}");
            foreach (var write in group) _constructorLiveFlags[write.Key] = flag;
        }
    }

    private void DestroyAssignmentTarget(AssignmentExpression node, TypeExpression type, string address)
    {
        if (_constructorLiveFlags.TryGetValue(node, out string? flag))
        {
            new ConditionalTemporaryCleanup(type, address, flag).Execute(this);
            Emit($"    store i1 true, i1* {flag}");
        }
        else if (!_typeChecker.IsInitialization(node)) EmitDestroy(type, address);
    }

    private void WithArgumentOwnership(Action body)
    {
        _argumentOwners.Push(new());
        try { body(); }
        finally { _argumentOwners.Pop(); }
    }

    private void TransferArguments()
    {
        if (_argumentOwners.Count == 0) return;
        foreach (var action in _argumentOwners.Peek())
            foreach (var scope in _deferScopes) scope.Remove(action);
        _argumentOwners.Peek().Clear();
    }

    private void ProtectArgument(TypeExpression type, string value)
    {
        string address = NewTemp(), llvmType = EmitType(type);
        Emit($"    {address} = alloca {llvmType}");
        Emit($"    store {llvmType} {value}, {llvmType}* {address}");
        var cleanup = new TemporaryCleanup(type, address);
        _deferScopes[^1].Add(cleanup);
        _argumentOwners.Peek().Add(cleanup);
    }

    private void GuardPlacementAlignment(string pointer, string pointerType, TypeExpression type)
    {
        int alignment = _typeChecker.GetTypeAlignment(type);
        string address = NewTemp(), remainder = NewTemp(), aligned = NewTemp();
        string ok = NewLabel("placement_aligned"), fail = NewLabel("placement_misaligned");
        Emit($"    {address} = ptrtoint {pointerType} {pointer} to i{TargetInfo.Default.PointerBits}");
        Emit($"    {remainder} = urem i{TargetInfo.Default.PointerBits} {address}, {alignment}");
        Emit($"    {aligned} = icmp eq i{TargetInfo.Default.PointerBits} {remainder}, 0");
        Emit($"    br i1 {aligned}, label %{ok}, label %{fail}");
        Emit($"{fail}:");
        Emit("    call void @llvm.trap()");
        Emit("    unreachable");
        Emit($"{ok}:");
    }

    public void Visit(MoveExpression node)
    {
        if (_typeChecker.GetMove(node) is NewExpression creation) creation.Accept(this);
        else
        {
            TypeExpression type = _typeChecker.GetType(node.Operand);
            EmitValueForTarget(node.Operand, type);
            if (node.Destination != null)
            {
                string value = Pop();
                node.Destination.Accept(this);
                string address = Pop(), llvm = EmitType(type);
                GuardNonNull(address, llvm + "*");
                GuardPlacementAlignment(address, llvm + "*", type);
                Emit($"    store {llvm} {value}, {llvm}* {address}");
                Push(address);
            }
        }
    }
}
