using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private Stack<string> _caughtHandles => CurrentFunction.CaughtHandles;
    private bool WindowsEh => TargetInfo.Default.Triple.Contains("windows");

    private void EmitNativeInvoke(string prefix, string invocation)
    {
        var function = CurrentFunction;
        function.NeedsPersonality = true;
        string normal = NewLabel("invoke_next"), unwind = NewLabel("invoke_unwind");
        Emit(prefix + "invoke " + invocation + $" to label %{normal} unwind label %{unwind}");
        Emit(unwind + ":");
        bool savedTerminated = _hasTerminated, savedCleanup = function.InCleanup;
        int savedTryDepth = function.CleanupTryDepth;
        _hasTerminated = false;
        // Retain the native exception, then leave its catch funclet before running
        // language cleanup. This gives defer blocks ordinary control flow (including
        // their own try/catch), on both MSVC and Itanium runtimes.
        string handle = EmitCaptureOutsideFunclet();
        TryBlockInfo? handler = _emitterTryStack.Count > (function.InCleanup ? function.CleanupTryDepth : 0)
            ? _emitterTryStack.Peek() : null;
        if (function.InCleanup && handler == null)
            EmitTermination(handle);
        else
        {
            function.InCleanup = true;
            function.CleanupTryDepth = _emitterTryStack.Count;
            _exceptionCleanup++;
            try { EmitDefersDownTo(handler?.DeferDepth ?? 0); }
            finally { _exceptionCleanup--; }
            if (!_hasTerminated)
            {
                if (handler != null)
                {
                    Emit($"    store i8* {handle}, i8** {handler.OwnedSlot}");
                    Emit($"    br label %{handler.DispatchLabel}");
                }
                else if (function.ExceptionBoundary) EmitTermination(handle);
                else
                {
                    // This resumes the unwind after this function's cleanup.
                    EmitRuntimeCall(ExceptionRuntime.Rethrow, $"i8* {handle}", handleUnwind: false);
                    Emit("    unreachable");
                }
            }
        }
        function.InCleanup = savedCleanup;
        function.CleanupTryDepth = savedTryDepth;
        _hasTerminated = savedTerminated;
        Emit(normal + ":");
    }

    private string EmitCaptureOutsideFunclet()
    {
        string handle = NewTemp();
        if (WindowsEh)
        {
            string dispatch = NewTemp(), pad = NewTemp(), caught = NewLabel("native_catch"), continuation = NewLabel("native_caught");
            Emit($"    {dispatch} = catchswitch within none [label %{caught}] unwind to caller");
            Emit(caught + ":");
            Emit($"    {pad} = catchpad within {dispatch} [i8* null, i32 64, i8* null]");
            EmitRuntimeCall(ExceptionRuntime.Capture, "", handle, funclet: pad);
            Emit($"    catchret from {pad} to label %{continuation}");
            Emit(continuation + ":");
        }
        else
        {
            string landing = NewTemp(), pointer = NewTemp();
            Emit($"    {landing} = landingpad {{ i8*, i32 }} catch i8* null");
            Emit($"    {pointer} = extractvalue {{ i8*, i32 }} {landing}, 0");
            EmitCallInstruction("i8*", "@__cxa_begin_catch", $"i8* {pointer}", handleUnwind: false);
            EmitRuntimeCall(ExceptionRuntime.Capture, "", handle);
            EmitCallInstruction("void", "@__cxa_end_catch", "", handleUnwind: false);
        }
        return handle;
    }

    private void EmitTermination(string handle)
    {
        var declaration = _typeChecker.GetUnhandledExceptionHook();
        string hook = declaration != null
            ? "bitcast (void (%Exception*)* @gflat$Exceptions$Unhandled" + _typeChecker.GetOverloadSuffix(declaration) + " to void (i8*)*)" : "null";
        EmitRuntimeCall(ExceptionRuntime.Terminate, $"i8* {handle}, void (i8*)* {hook}");
        Emit("    unreachable");
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
            EmitRuntimeCall(ExceptionRuntime.Rethrow, $"i8* {handle}");
        }
        else
        {
            var type = _typeChecker.GetType(node.Expression);
            string llvm = EmitType(type), size = NewTemp(), bytes = NewTemp(), storage = NewTemp(), address = NewTemp();
            Emit($"    {size} = getelementptr {llvm}, {llvm}* null, i32 1");
            Emit($"    {bytes} = ptrtoint {llvm}* {size} to i64");
            EmitRuntimeCall(ExceptionRuntime.Allocate, $"i64 {bytes}, i8* (i64)* @{RuntimeMemorySymbol("Allocate")}", storage);
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
            EmitRuntimeCall(ExceptionRuntime.Throw, $"i8* {storage}, void (i8*)* bitcast (void (%Exception*)* @gflat_destroy_exception to void (i8*)*)");
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
        EmitRuntimeCall(ExceptionRuntime.Object, $"i8* {handle}", rawObject);
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
                EmitCallInstruction("zeroext i1", "@gflat_is_instance", $"i8** {vt}, i8** bitcast ([{cls.VirtualMethods.Count} x i8*]* @{cls.Name}$vtable to i8**)", matches, handleUnwind: false);
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
        EmitRuntimeCall(ExceptionRuntime.Rethrow, $"i8* {handle}");
        Emit("    unreachable");
        Emit(end + ":");
        _hasTerminated = false;
    }
}
