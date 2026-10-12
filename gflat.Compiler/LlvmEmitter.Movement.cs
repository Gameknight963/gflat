using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly Dictionary<string, string> _ownerLiveFlags = new();

    private IDeferAction RegisterOwner(TypeExpression type, string address, bool live)
    {
        string flag = NewTemp();
        Emit($"    {flag} = alloca i1");
        Emit($"    store i1 {(live ? "true" : "false")}, i1* {flag}");
        _ownerLiveFlags[address] = flag;
        return new ConditionalTemporaryCleanup(type, address, flag);
    }

    private void SetOwnerLive(string address, bool live)
    {
        if (_ownerLiveFlags.TryGetValue(address, out string? flag))
            Emit($"    store i1 {(live ? "true" : "false")}, i1* {flag}");
    }

    private readonly Stack<List<IDeferAction>> _argumentOwners = new();
    private readonly List<IDeferAction> _parameterOwners = new();
    private readonly Dictionary<AssignmentExpression, string> _constructorLiveFlags = new();

    private void DestroyAssignmentTarget(AssignmentExpression node, TypeExpression type, string address)
    {
        if (_constructorLiveFlags.TryGetValue(node, out string? flag))
        {
            new ConditionalTemporaryCleanup(type, address, flag).Execute(this);
            Emit($"    store i1 true, i1* {flag}");
        }
        else if (_ownerLiveFlags.TryGetValue(address, out string? live))
        {
            new ConditionalTemporaryCleanup(type, address, live).Execute(this);
            Emit($"    store i1 true, i1* {live}");
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
        TypeExpression type = _typeChecker.GetType(node.Operand);
        string llvm = EmitType(type);
        string? destination = null;
        if (node.Destination != null)
        {
            node.Destination.Accept(this);
            destination = Pop();
            GuardNonNull(destination, llvm + "*");
            GuardPlacementAlignment(destination, llvm + "*", type);
        }
        EmitAddress(node.Operand);
        string source = Pop(), value = NewTemp();
        Emit($"    {value} = load {llvm}, {llvm}* {source}");
        SetOwnerLive(source, false);
        if (destination != null)
        {
            Emit($"    store {llvm} {value}, {llvm}* {destination}");
            Push(destination);
        }
        else Push(value);
    }
}
