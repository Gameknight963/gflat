using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool _ehFunction, _ehBoundary;
    private bool _ehCleanup;
    private readonly Stack<string> _caughtHandles = new();
    private bool WindowsEh => TargetInfo.Default.Triple.Contains("windows");
    private string NativeDeclarations => """
        declare i8* @__gflat_eh_allocate_exception(i64, i8* (i64)*)
        declare void @__gflat_eh_free_exception(i8*, void (i8*)*)
        declare void @__gflat_eh_throw(i8*, void (i8*)*) noreturn
        declare i8* @__gflat_eh_capture() nounwind
        declare i8* @__gflat_eh_object(i8*) nounwind
        declare void @__gflat_eh_release(i8*) nounwind
        declare void @__gflat_eh_rethrow(i8*) noreturn
        declare void @__gflat_eh_terminate(i8*, void (i8*)*) noreturn nounwind
        """ + "\n" + (WindowsEh ? "declare i32 @__CxxFrameHandler3(...)\n" : "declare i32 @__gxx_personality_v0(...)\ndeclare i8* @__cxa_begin_catch(i8*) nounwind\ndeclare void @__cxa_end_catch() nounwind\n");

    private void Raw(string text) => _output.AppendLine(text);

    private void EmitNativeLine(string line)
    {
        if (line.StartsWith("define ", StringComparison.Ordinal))
        {
            BeginCollectorFunction(line);
            _ehFunction = true;
            _ehBoundary = line.Contains("@main(") || line.Contains("$dtor(") || line.Contains("@gflat_destroy_exception(");
            string personality = WindowsEh ? "__CxxFrameHandler3" : "__gxx_personality_v0";
            Raw(line[..^1] + $"personality i32 (...)* @{personality} {{");
            return;
        }
        if (line == "}") { EndCollectorFunction(); _ehFunction = false; Raw(line); return; }
        if (line == "entry:") { Raw(line); BeginCollectorEntry(); return; }
        int call = line.IndexOf("call ", StringComparison.Ordinal);
        if (!_ehFunction || call < 0 || line.Contains("@llvm.") ||
            line.Contains("@gflat_is_instance(") || line.Contains("@gflat_get_parent_vtable(") ||
            line.Contains("@__cxa_") ||
            line.Contains("@__gflat_eh_") && !line.Contains("@__gflat_eh_throw(") && !line.Contains("@__gflat_eh_rethrow(")
            && !line.Contains("@__gflat_eh_allocate_exception") && !line.Contains("@__gflat_eh_free_exception"))
        {
            Raw(call >= 0 ? line : line);
            return;
        }
        if (!_typeChecker.UsesCollector && !_ehCleanup && !_ehBoundary && _emitterTryStack.Count == 0 && !_deferScopes.Any(s => s.Count != 0))
        {
            Raw(line);
            return;
        }
        EmitNativeInvoke(line, call);
    }

    private void EmitNativeInvoke(string line, int call)
    {
        string normal = NewLabel("invoke_next"), unwind = NewLabel("invoke_unwind");
        Raw(line[..call] + "invoke " + line[(call + 5)..] + $" to label %{normal} unwind label %{unwind}");
        Raw(unwind + ":");
        bool savedTerminated = _hasTerminated, savedCleanup = _ehCleanup;
        int savedTryDepth = _ehCleanupTryDepth;
        _hasTerminated = false;
        // Retain the native exception, then leave its catch funclet before running
        // language cleanup. This gives defer blocks ordinary control flow (including
        // their own try/catch), on both MSVC and Itanium runtimes.
        string handle = EmitCaptureOutsideFunclet();
        TryBlockInfo? handler = _emitterTryStack.Count > (_ehCleanup ? _ehCleanupTryDepth : 0)
            ? _emitterTryStack.Peek() : null;
        if (_ehCleanup && handler == null)
            EmitTermination(handle);
        else
        {
            _ehCleanup = true;
            _ehCleanupTryDepth = _emitterTryStack.Count;
            _exceptionCleanup++;
            try { EmitDefersDownTo(handler?.DeferDepth ?? 0); }
            finally { _exceptionCleanup--; }
            if (!_hasTerminated)
            {
                if (handler != null)
                {
                    Raw($"    store i8* {handle}, i8** {handler.OwnedSlot}");
                    Raw($"    br label %{handler.DispatchLabel}");
                }
                else if (_ehBoundary) EmitTermination(handle);
                else
                {
                    Raw($"    call void @__gflat_eh_rethrow(i8* {handle})");
                    Raw("    unreachable");
                }
            }
        }
        _ehCleanup = savedCleanup;
        _ehCleanupTryDepth = savedTryDepth;
        _hasTerminated = savedTerminated;
        Raw(normal + ":");
    }
    private int _ehCleanupTryDepth;

    private string EmitCaptureOutsideFunclet()
    {
        string handle = NewTemp();
        if (WindowsEh)
        {
            string dispatch = NewTemp(), pad = NewTemp(), caught = NewLabel("native_catch"), continuation = NewLabel("native_caught");
            Raw($"    {dispatch} = catchswitch within none [label %{caught}] unwind to caller");
            Raw(caught + ":");
            Raw($"    {pad} = catchpad within {dispatch} [i8* null, i32 64, i8* null]");
            Raw($"    {handle} = call i8* @__gflat_eh_capture() [ \"funclet\"(token {pad}) ]");
            Raw($"    catchret from {pad} to label %{continuation}");
            Raw(continuation + ":");
        }
        else
        {
            string landing = NewTemp(), pointer = NewTemp();
            Raw($"    {landing} = landingpad {{ i8*, i32 }} catch i8* null");
            Raw($"    {pointer} = extractvalue {{ i8*, i32 }} {landing}, 0");
            Raw($"    call i8* @__cxa_begin_catch(i8* {pointer})");
            Raw($"    {handle} = call i8* @__gflat_eh_capture()");
            Raw("    call void @__cxa_end_catch()");
        }
        return handle;
    }

    private void EmitTermination(string handle)
    {
        var declaration = _typeChecker.GetUnhandledExceptionHook();
        string hook = declaration != null
            ? "bitcast (void (%Exception*)* @gflat$Exceptions$Unhandled" + _typeChecker.GetOverloadSuffix(declaration) + " to void (i8*)*)" : "null";
        Raw($"    call void @__gflat_eh_terminate(i8* {handle}, void (i8*)* {hook})");
        Raw("    unreachable");
    }

    private void GuardAllocation(NewExpression node, string storage)
    {
        if (_typeChecker.GetAllocationFailure(node) is not { } failure)
        {
            GuardNonNull(storage, "i8*");
            return;
        }
        string failed = NewTemp(), error = NewLabel("allocation_failure"), success = NewLabel("allocation_success");
        Emit($"    {failed} = icmp eq i8* {storage}, null");
        Emit($"    br i1 {failed}, label %{error}, label %{success}");
        Emit(error + ":");
        bool saved = _hasTerminated;
        EmitNativeThrow(failure);
        _hasTerminated = saved;
        Emit(success + ":");
    }

    private void EmitNativeThrow(ThrowStatement node)
    {
        if (node.Expression == null)
        {
            string slot = _caughtHandles.Peek(), handle = NewTemp();
            Emit($"    {handle} = load i8*, i8** {slot}");
            Emit($"    store i8* null, i8** {slot}");
            Emit($"    call void @__gflat_eh_rethrow(i8* {handle})");
        }
        else
        {
            var type = _typeChecker.GetType(node.Expression);
            string llvm = EmitType(type), size = NewTemp(), bytes = NewTemp(), storage = NewTemp(), address = NewTemp();
            Emit($"    {size} = getelementptr {llvm}, {llvm}* null, i32 1");
            Emit($"    {bytes} = ptrtoint {llvm}* {size} to i64");
            Emit($"    {storage} = call i8* @__gflat_eh_allocate_exception(i64 {bytes}, i8* (i64)* @{RuntimeMemorySymbol("Allocate")})");
            GuardNonNull(storage, "i8*");
            Emit($"    {address} = bitcast i8* {storage} to {llvm}*");
            _deferScopes.Add(new() { new ExceptionOnlyCleanup(new ExceptionAllocationCleanup(storage)) });
            EmitValueForTarget(node.Expression, type);
            string value = Pop();
            EmitManagedStore(type, value, address, initialize: true);
            // Throwing a value constructs its static type. In particular, copying
            // a base catch binding must not retain a derived vtable in storage
            // sized only for the base. `throw;` preserves the dynamic object.
            var exceptionClass = _typeChecker.GetClassByIdentity(((NamedTypeExpression)_typeChecker.ResolveAlias(type)).Name)!;
            string vtable = NewTemp();
            Emit($"    {vtable} = getelementptr {llvm}, {llvm}* {address}, i32 0, i32 0");
            Emit($"    store i8** bitcast ([{exceptionClass.VirtualMethods.Count} x i8*]* @{exceptionClass.Name}$vtable to i8**), i8*** {vtable}");
            _deferScopes.RemoveAt(_deferScopes.Count - 1);
            Emit($"    call void @__gflat_eh_throw(i8* {storage}, void (i8*)* bitcast (void (%Exception*)* @gflat_destroy_exception to void (i8*)*))");
        }
        Emit("    unreachable");
        _hasTerminated = true;
    }

    private void EmitNativeTry(TryStatement node)
    {
        string slot = NewTemp();
        Emit($"    {slot} = alloca i8*");
        Emit($"    store i8* null, i8** {slot}");
        string dispatch = NewLabel("catch_dispatch"), unwind = NewLabel("catch_unwind"), end = NewLabel("try_end");
        var info = new TryBlockInfo(node, slot, dispatch, _deferScopes.Count, unwind);
        _emitterTryStack.Push(info);
        node.TryBlock.Accept(this);
        _emitterTryStack.Pop();
        if (!_hasTerminated) Emit($"    br label %{end}");
        Emit(dispatch + ":");
        _hasTerminated = false;
        string handle = NewTemp(), rawObject = NewTemp(), error = NewTemp();
        Emit($"    {handle} = load i8*, i8** {slot}");
        Emit($"    {rawObject} = call i8* @__gflat_eh_object(i8* {handle})");
        Emit($"    {error} = bitcast i8* {rawObject} to %Exception*");
        foreach (var clause in node.CatchClauses)
        {
            string body = NewLabel("catch_body"), next = NewLabel("catch_next");
            if (clause.ExceptionType != null)
            {
                string isNative = NewTemp(), check = NewLabel("catch_type");
                Emit($"    {isNative} = icmp ne %Exception* {error}, null");
                Emit($"    br i1 {isNative}, label %{check}, label %{next}");
                Emit(check + ":");
                var binding = _typeChecker.ResolveAlias(clause.ExceptionType);
                var inner = binding is ReferenceTypeExpression reference ? reference.Inner : ((PointerTypeExpression)binding).Inner;
                var named = (NamedTypeExpression)_typeChecker.ResolveAlias(inner);
                var cls = _typeChecker.GetClassByIdentity(named.Name)!;
                string vtSlot = NewTemp(), vt = NewTemp(), matches = NewTemp();
                Emit($"    {vtSlot} = getelementptr %Exception, %Exception* {error}, i32 0, i32 0");
                Emit($"    {vt} = load i8**, i8*** {vtSlot}");
                Emit($"    {matches} = call i1 @gflat_is_instance(i8** {vt}, i8** bitcast ([{cls.VirtualMethods.Count} x i8*]* @{cls.Name}$vtable to i8**))");
                Emit($"    br i1 {matches}, label %{body}, label %{next}");
            }
            else Emit($"    br label %{body}");
            Emit(body + ":");
            _hasTerminated = false;
            string? previous = null;
            if (clause.VariableName != null)
            {
                _locals.TryGetValue(clause.VariableName, out previous);
                string typed = NewTemp(), variable = NewTemp(), llvm = EmitType(clause.ExceptionType!);
                Emit($"    {typed} = bitcast %Exception* {error} to {llvm}");
                if (clause.ExceptionType is ReferenceTypeExpression) _locals[clause.VariableName] = typed;
                else
                {
                    Emit($"    {variable} = alloca {llvm}");
                    Emit($"    store {llvm} {typed}, {llvm}* {variable}");
                    _locals[clause.VariableName] = variable;
                }
            }
            var cleanup = new CatchCleanupAction(slot);
            _deferScopes.Add(new() { cleanup });
            _caughtHandles.Push(slot);
            clause.Body.Accept(this);
            _caughtHandles.Pop();
            if (!_hasTerminated) { cleanup.Execute(this); Emit($"    br label %{end}"); }
            _deferScopes.RemoveAt(_deferScopes.Count - 1);
            if (clause.VariableName != null)
            {
                if (previous == null) _locals.Remove(clause.VariableName);
                else _locals[clause.VariableName] = previous;
            }
            Emit(next + ":");
            _hasTerminated = false;
        }
        Emit($"    store i8* null, i8** {slot}");
        Emit($"    call void @__gflat_eh_rethrow(i8* {handle})");
        Emit("    unreachable");
        Emit(end + ":");
        _hasTerminated = false;
    }
}
