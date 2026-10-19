using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private void EmitAlloca(string type, string address)
    {
        string line = $"    {address} = alloca {type}";
        if (_typeChecker.UsesCollector && _functions.Count != 0) CurrentFunction.ManagedPrologue.AppendLine(line);
        else Emit(line);
    }
    private bool HasManagedReferences(TypeExpression type, HashSet<string>? visited = null)
    {
        type = _typeChecker.ResolveAlias(type);
        if (type is ManagedTypeExpression) return true;
        if (type is ArrayTypeExpression array) return HasManagedReferences(array.ElementType, visited);
        // A raw pointer or ref parameter borrows storage, rather than containing its fields.
        if (type is not NamedTypeExpression named || TypeChecker.IsPrimitive(named.Name)) return false;
        visited ??= new();
        if (!visited.Add(named.Name)) return false;
        var fields = _typeChecker.GetStructByIdentity(named.Name)?.Fields.Select(f => f.Type) ?? _typeChecker.GetClassByIdentity(named.Name)?.Fields.Select(f => f.Type);
        return fields?.Any(f => HasManagedReferences(f, visited)) == true;
    }
    private bool NeedsCleanup(TypeExpression type)
        => _typeChecker.HasDestructor(type) || (_typeChecker.UsesCollector && HasManagedReferences(type));

    private void EmitManagedReferences(TypeExpression type, string address, bool added, bool initialize = false)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(type)) return;
        type = _typeChecker.ResolveAlias(type);
        string llvm = EmitType(type);
        if (type is ManagedTypeExpression reference)
        {
            var collector = _typeChecker.GetCollectorBinding(reference);
            string hook = CollectorMethodName(collector, added ? collector.ReferenceAdded : collector.ReferenceRemoved);
            if (initialize) Emit($"    store {llvm} zeroinitializer, {llvm}* {address}");
            string raw = NewTemp();
            Emit($"    {raw} = bitcast {llvm}* {address} to i8*");
            EmitCallInstruction("void", "@" + hook, $"i8* {raw}", handleUnwind: false);
            if (!added) Emit($"    store {llvm} zeroinitializer, {llvm}* {address}");
        }
        else if (type is ArrayTypeExpression { Size: not null } array)
        {
            string index = NewTemp(), test = NewLabel("gc_element_test"), body = NewLabel("gc_element"), done = NewLabel("gc_elements_done");
            EmitAlloca("i32", index);
            Emit($"    store i32 0, i32* {index}");
            Emit($"    br label %{test}"); Emit(test + ":");
            string i = NewTemp(), more = NewTemp();
            Emit($"    {i} = load i32, i32* {index}");
            Emit($"    {more} = icmp ult i32 {i}, {array.Size.Value}");
            Emit($"    br i1 {more}, label %{body}, label %{done}"); Emit(body + ":");
            string element = NewTemp();
            Emit($"    {element} = getelementptr {llvm}, {llvm}* {address}, i32 0, i32 {i}");
            EmitManagedReferences(array.ElementType, element, added, initialize);
            string next = NewTemp(); Emit($"    {next} = add i32 {i}, 1");
            Emit($"    store i32 {next}, i32* {index}"); Emit($"    br label %{test}"); Emit(done + ":");
        }
        else if (type is NamedTypeExpression named)
        {
            var structure = _typeChecker.GetStructByIdentity(named.Name);
            var cls = _typeChecker.GetClassByIdentity(named.Name);
            var fields = (structure?.Fields.Select(f => f.Type) ?? cls!.Fields.Select(f => f.Type)).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                if (!HasManagedReferences(fields[i])) continue;
                string field = NewTemp();
                Emit($"    {field} = getelementptr {llvm}, {llvm}* {address}, i32 0, i32 {i + (cls != null ? 1 : 0)}");
                EmitManagedReferences(fields[i], field, added, initialize);
            }
        }
    }
    private void EmitManagedStore(TypeExpression type, string value, string address, bool initialize = false)
    {
        if (!initialize) EmitManagedReferences(type, address, false);
        string llvm = EmitType(type);
        Emit($"    store {llvm} {value}, {llvm}* {address}");
        EmitManagedReferences(type, address, true);
    }
    private sealed class ManagedReferenceCleanup(TypeExpression type, string address, string? flag = null) : IDeferAction
    {
        public void Execute(LlvmEmitter e)
        {
            string? done = null;
            if (flag != null)
            {
                string active = e.NewTemp(), remove = e.NewLabel("gc_remove");
                done = e.NewLabel("gc_removed");
                e.Emit($"    {active} = load i1, i1* {flag}");
                e.Emit($"    br i1 {active}, label %{remove}, label %{done}"); e.Emit(remove + ":");
                e.Emit($"    store i1 false, i1* {flag}");
            }
            e.EmitManagedReferences(type, address, false);
            if (done != null) { e.Emit($"    br label %{done}"); e.Emit(done + ":"); }
        }
    }
    private string CreateManagedTemporary(TypeExpression type, string? initialValue = null)
    {
        string address = NewTemp(), flag = NewTemp(), llvm = EmitType(type);
        EmitAlloca(llvm, address); EmitAlloca("i1", flag);
        var prologue = CurrentFunction.ManagedPrologue;
        prologue.AppendLine($"    store {llvm} zeroinitializer, {llvm}* {address}");
        prologue.AppendLine($"    store i1 false, i1* {flag}");
        // Register the initial contents once. An already evaluated value can
        // be published directly instead of registering null and removing it again.
        Emit($"    store {llvm} {initialValue ?? "zeroinitializer"}, {llvm}* {address}");
        EmitManagedReferences(type, address, true);
        Emit($"    store i1 true, i1* {flag}");
        var cleanup = new ManagedReferenceCleanup(type, address, flag);
        if (_deferScopes.Count != 0) _deferScopes[^1].Add(cleanup);
        else _parameterOwners.Add(cleanup);
        return address;
    }
    private void ProtectManagedValue(TypeExpression type, string value)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(type)) return;
        CreateManagedTemporary(type, value);
    }
    private IDeferAction? RegisterManagedCleanup(TypeExpression type, string address, bool onExceptionOnly = false)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(type)) return null;
        IDeferAction cleanup = new ManagedReferenceCleanup(type, address);
        if (onExceptionOnly) cleanup = new ExceptionOnlyCleanup(cleanup);
        _deferScopes[^1].Add(cleanup);
        return cleanup;
    }
    private void PushManaged(AstNode node, string value)
    {
        ProtectManagedValue(_typeChecker.GetType(node), value);
        Push(value);
    }
    private void InitializeManagedLocal(TypeExpression type, string address, int scope)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(type)) return;
        EmitManagedReferences(type, address, true, initialize: true);
        if (!_typeChecker.HasDestructor(type)) _deferScopes[scope].Add(new ManagedReferenceCleanup(type, address));
        else
        {
            // Until a value is constructed, no destructor owns these registrations.
            // Keep cleanup active through failed initialization and unused locals.
            _deferScopes[scope].Add(TrackPendingOwnerRoots(type, address, true));
        }
    }
    private ManagedReferenceCleanup TrackPendingOwnerRoots(TypeExpression type, string address, bool pending)
    {
        string flag = NewTemp();
        EmitAlloca("i1", flag);
        Emit($"    store i1 {(pending ? "true" : "false")}, i1* {flag}");
        CurrentFunction.PendingOwnerRoots[address] = flag;
        return new ManagedReferenceCleanup(type, address, flag);
    }
    private void CompleteManagedOwnerInitialization(string address)
    {
        if (_functions.Count != 0 && CurrentFunction.PendingOwnerRoots.TryGetValue(address, out string? pending))
            Emit($"    store i1 false, i1* {pending}");
    }
    private void RemoveUninitializedManagedOwnerRoots(TypeExpression type, string address)
    {
        if (_functions.Count != 0 && CurrentFunction.PendingOwnerRoots.TryGetValue(address, out string? pending))
            new ManagedReferenceCleanup(type, address, pending).Execute(this);
    }
    private void RegisterMovedLocalStorage(TypeExpression type, string address)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(type)) return;
        // The value is gone, but the local slot still exists. Its managed fields
        // were zeroed on removal; track that empty storage until reinitialization
        // or scope exit. Raw relocation sources do not take this path.
        EmitManagedReferences(type, address, true);
        if (CurrentFunction.PendingOwnerRoots.TryGetValue(address, out string? pending))
            Emit($"    store i1 true, i1* {pending}");
    }
    private void RegisterManagedParameter(Parameter p, string address)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(p.Type)) return;
        EmitManagedReferences(p.Type, address, true);
        if (!_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(new ManagedReferenceCleanup(p.Type, address));
        else _parameterOwners.Add(TrackPendingOwnerRoots(p.Type, address, false));
    }
    private string CollectorMethodName(TypeChecker.CollectorBinding collector, MethodDeclaration method)
    {
        string owner = collector.IsClass
            ? _typeChecker.GetClassByIdentity(collector.Owner)!.Methods[method.Name].DeclaringClass : collector.Owner;
        return GetMethodMangledName(owner, method, _typeChecker.GetFunctionNamespace(method), collector.IsClass);
    }

    private string EmitManagedAllocation(NewExpression node, string bytes)
    {
        var reference = (ManagedTypeExpression)_typeChecker.ResolveAlias(_typeChecker.GetType(node));
        var collector = _typeChecker.GetCollectorBinding(reference);
        string result = NewTemp();
        EmitCallInstruction("i8*", "@" + CollectorMethodName(collector, collector.Allocate),
            $"i{TargetInfo.Default.PointerBits} {bytes}, i{TargetInfo.Default.PointerBits} {_typeChecker.GetTypeAlignment(node.Type)}", result);
        return result;
    }
}
