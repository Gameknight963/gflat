using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly Dictionary<string, string> _ownerLiveFlags = new();

    private IDeferAction RegisterOwner(TypeExpression type, string address, bool live)
    {
        string flag = NewTemp();
        EmitAlloca("i1", flag);
        Emit($"    store i1 {(live ? "true" : "false")}, i1* {flag}");
        _ownerLiveFlags[address] = flag;
        if (live) CompleteManagedOwnerInitialization(address);
        return new ConditionalTemporaryCleanup(type, address, flag);
    }

    private void SetOwnerLive(string address, bool live)
    {
        if (_ownerLiveFlags.TryGetValue(address, out string? flag))
            Emit($"    store i1 {(live ? "true" : "false")}, i1* {flag}");
    }

    private readonly Stack<List<(IDeferAction Cleanup, TypeExpression Type, string Address)>> _argumentOwners = new();
    private readonly List<IDeferAction> _parameterOwners = new();
    private readonly Dictionary<AssignmentExpression, string> _constructorLiveFlags = new();

    private string PreserveClassAssignmentHeader(AssignmentExpression node, TypeExpression type, string address, string value)
    {
        if (_typeChecker.ResolveAlias(type) is not NamedTypeExpression named ||
            _typeChecker.GetClassByIdentity(named.Name) is not { } cls) return value;
        string? live = _constructorLiveFlags.GetValueOrDefault(node) ?? _ownerLiveFlags.GetValueOrDefault(address);
        if (live == null && _typeChecker.IsInitialization(node)) return value;
        string llvm = EmitType(type), header;
        string LoadHeader()
        {
            string slot = NewTemp(), old = NewTemp();
            Emit($"    {slot} = getelementptr {llvm}, {llvm}* {address}, i32 0, i32 0");
            Emit($"    {old} = load i8**, i8*** {slot}");
            return old;
        }
        if (live == null) header = LoadHeader();
        else
        {
            string flag = NewTemp(), initialized = NewLabel("class_assignment_live"), empty = NewLabel("class_assignment_empty"), ready = NewLabel("class_assignment_header");
            Emit($"    {flag} = load i1, i1* {live}");
            Emit($"    br i1 {flag}, label %{initialized}, label %{empty}"); Emit(initialized + ":");
            string old = LoadHeader();
            Emit($"    br label %{ready}"); Emit(empty + ":");
            Emit($"    br label %{ready}"); Emit(ready + ":");
            header = NewTemp();
            Emit($"    {header} = phi i8** [ {old}, %{initialized} ], [ bitcast ([{cls.VirtualMethods.Count} x i8*]* @{cls.Name}$vtable to i8**), %{empty} ]");
        }
        string result = NewTemp();
        Emit($"    {result} = insertvalue {llvm} {value}, i8** {header}, 0");
        return result;
    }

    private void DestroyAssignmentTarget(AssignmentExpression node, TypeExpression type, string address)
    {
        if (_constructorLiveFlags.TryGetValue(node, out string? flag))
        {
            if (HasManagedReferences(type))
            {
                string live = NewTemp(), empty = NewLabel("constructor_empty_roots"), ready = NewLabel("constructor_owned_roots");
                Emit($"    {live} = load i1, i1* {flag}");
                Emit($"    br i1 {live}, label %{ready}, label %{empty}");
                Emit(empty + ":");
                EmitManagedReferences(type, address, false);
                Emit($"    br label %{ready}"); Emit(ready + ":");
            }
            new ConditionalTemporaryCleanup(type, address, flag).Execute(this);
            Emit($"    store i1 true, i1* {flag}");
        }
        else if (_ownerLiveFlags.TryGetValue(address, out string? live))
        {
            RemoveUninitializedManagedOwnerRoots(type, address);
            new ConditionalTemporaryCleanup(type, address, live).Execute(this);
            Emit($"    store i1 true, i1* {live}");
            CompleteManagedOwnerInitialization(address);
        }
        else if (!_typeChecker.IsInitialization(node))
        {
            // Replacing a base subobject must not destroy the derived tail.
            if (_typeChecker.ResolveAlias(type) is NamedTypeExpression named &&
                _typeChecker.GetClassByIdentity(named.Name) is { } cls)
                new ClassDestructorDeferAction(cls, address, false, cls.DestructorSlot).Execute(this);
            else EmitDestroy(type, address);
        }
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
        // The callee takes destruction responsibility. Its parameter roots are
        // established on entry, so retain caller roots through the whole call.
        foreach (var argument in _argumentOwners.Peek())
            foreach (var scope in _deferScopes)
                if (scope.Remove(argument.Cleanup) && _typeChecker.UsesCollector && HasManagedReferences(argument.Type))
                    scope.Add(new ManagedReferenceCleanup(argument.Type, argument.Address));
        _argumentOwners.Peek().Clear();
    }

    private void ProtectArgument(TypeExpression type, string value)
    {
        string address = NewTemp(), llvmType = EmitType(type);
        EmitAlloca(llvmType, address);
        EmitManagedStore(type, value, address, initialize: true);
        var cleanup = new TemporaryCleanup(type, address);
        _deferScopes[^1].Add(cleanup);
        _argumentOwners.Peek().Add((cleanup, type, address));
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
        EmitCallInstruction("void", "@llvm.trap", "", handleUnwind: false);
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
        ProtectManagedValue(type, value);
        EmitManagedReferences(type, source, false);
        SetOwnerLive(source, false);
        if (node.Operand is IdentifierExpression) RegisterMovedLocalStorage(type, source);
        if (destination != null)
        {
            EmitManagedStore(type, value, destination, initialize: true);
            Push(destination);
        }
        else Push(value);
    }
}
