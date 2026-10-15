using System.Text;
using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private sealed class CollectorFunction(StringBuilder output)
    {
        public StringBuilder Output { get; } = output;
        public StringBuilder Prologue { get; } = new();
        public int EntryPosition;
        public bool NativeAllocate, NativeFree;
        public string FirstParameter = "";
    }
    private readonly Stack<CollectorFunction> _collectorFunctions = new();

    private void BeginCollectorFunction(string declaration)
    {
        if (!_typeChecker.UsesCollector) return;
        var f = new CollectorFunction(_output) {
            NativeAllocate = _typeChecker.HasCollectorNativeTracking && declaration.Contains("@gflat$Memory$Allocate("),
            NativeFree = _typeChecker.HasCollectorNativeTracking && declaration.Contains("@gflat$Memory$Free(")
        };
        if (f.NativeAllocate || f.NativeFree)
        {
            string p = declaration[(declaration.IndexOf('(') + 1)..].Split(',')[0];
            f.FirstParameter = p[p.IndexOf('%')..].Split(')')[0].Trim();
        }
        _collectorFunctions.Push(f);
    }
    private void BeginCollectorEntry()
    {
        if (_collectorFunctions.Count == 0) return;
        var f = _collectorFunctions.Peek();
        f.EntryPosition = _output.Length;
        if (f.NativeFree) Raw($"    call void @gflat$GC$ForgetNative(i8* {f.FirstParameter})");
    }
    private void EndCollectorFunction()
    {
        if (_collectorFunctions.Count == 0) return;
        var f = _collectorFunctions.Pop();
        f.Output.Insert(f.EntryPosition, f.Prologue);
    }
    private void EmitAlloca(string type, string address)
    {
        string line = $"    {address} = alloca {type}";
        if (_collectorFunctions.Count != 0) _collectorFunctions.Peek().Prologue.AppendLine(line);
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
        var fields = _typeChecker.GetStruct(named.Name)?.Fields.Select(f => f.Type) ?? _typeChecker.GetClass(named.Name)?.Fields.Select(f => f.Type);
        return fields?.Any(f => HasManagedReferences(f, visited)) == true;
    }
    private bool NeedsCleanup(TypeExpression type)
        => _typeChecker.HasDestructor(type) || (_typeChecker.UsesCollector && HasManagedReferences(type));

    private void EmitManagedReferences(TypeExpression type, string address, bool added, bool initialize = false)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(type)) return;
        type = _typeChecker.ResolveAlias(type);
        string llvm = EmitType(type);
        if (type is ManagedTypeExpression)
        {
            if (initialize) Raw($"    store {llvm} zeroinitializer, {llvm}* {address}");
            string raw = NewTemp();
            Raw($"    {raw} = bitcast {llvm}* {address} to i8*");
            Raw($"    call void @gflat$GC$Reference{(added ? "Added" : "Removed")}(i8* {raw})");
            if (!added) Raw($"    store {llvm} zeroinitializer, {llvm}* {address}");
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
            var structure = _typeChecker.GetStruct(named.Name);
            var cls = _typeChecker.GetClass(named.Name);
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
    private string CreateManagedTemporary(TypeExpression type)
    {
        string address = NewTemp(), flag = NewTemp(), llvm = EmitType(type);
        EmitAlloca(llvm, address); EmitAlloca("i1", flag);
        var prologue = _collectorFunctions.Peek().Prologue;
        prologue.AppendLine($"    store {llvm} zeroinitializer, {llvm}* {address}");
        prologue.AppendLine($"    store i1 false, i1* {flag}");
        Emit($"    store {llvm} zeroinitializer, {llvm}* {address}");
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
        EmitManagedStore(type, value, CreateManagedTemporary(type));
    }
    private void CleanupManagedStorage(TypeExpression type, string address)
    {
        if (_typeChecker.UsesCollector && HasManagedReferences(type))
            _deferScopes[^1].Add(new ManagedReferenceCleanup(type, address));
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
    }
    private void RegisterManagedParameter(Parameter p, string address)
    {
        if (!_typeChecker.UsesCollector || !HasManagedReferences(p.Type)) return;
        EmitManagedReferences(p.Type, address, true);
        if (!_typeChecker.HasDestructor(p.Type)) _parameterOwners.Add(new ManagedReferenceCleanup(p.Type, address));
    }
    private string EmitManagedAllocation(string bytes, TypeExpression type)
    {
        string result = NewTemp();
        Emit($"    {result} = call i8* @gflat$GC$Allocate(i64 {bytes}, i64 {_typeChecker.GetTypeAlignment(type)})");
        return result;
    }
    private void TrackNativeReturn(string value)
    {
        if (_collectorFunctions.Count == 0 || !_collectorFunctions.Peek().NativeAllocate) return;
        string tracked = NewTemp(), ok = NewLabel("native_tracked"), fail = NewLabel("native_untracked");
        Raw($"    {tracked} = call i1 @gflat$GC$TrackNative(i8* {value}, i64 {_collectorFunctions.Peek().FirstParameter})");
        Emit($"    br i1 {tracked}, label %{ok}, label %{fail}"); Emit(fail + ":");
        Emit($"    call void @gflat$Memory$Free(i8* {value})"); Emit("    ret i8* null"); Emit(ok + ":");
    }
}
