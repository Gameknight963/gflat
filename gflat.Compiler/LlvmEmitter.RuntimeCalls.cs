namespace gflat;

public partial class LlvmEmitter
{
    private sealed record RuntimeFunction(string Name, string ReturnType, string Parameters,
        bool CanUnwind = true, bool NoReturn = false)
    {
        public string Declaration => $"declare {ReturnType} @{Name}({Parameters})" +
            (NoReturn ? " noreturn" : "") + (CanUnwind ? "" : " nounwind");
    }

    private static class ExceptionRuntime
    {
        public static readonly RuntimeFunction Allocate = new("__gflat_eh_allocate_exception", "i8*", "i64, i8* (i64)*");
        public static readonly RuntimeFunction Free = new("__gflat_eh_free_exception", "void", "i8*, void (i8*)*");
        public static readonly RuntimeFunction Throw = new("__gflat_eh_throw", "void", "i8*, void (i8*)*", NoReturn: true);
        public static readonly RuntimeFunction ThrowDebug = new("__gflat_eh_throw_debug", "void", "i8*, void (i8*)*, i8*, i64, i64", NoReturn: true);
        public static readonly RuntimeFunction Capture = new("__gflat_eh_capture", "i8*", "", CanUnwind: false);
        public static readonly RuntimeFunction Object = new("__gflat_eh_object", "i8*", "i8*", CanUnwind: false);
        public static readonly RuntimeFunction Release = new("__gflat_eh_release", "void", "i8*", CanUnwind: false);
        public static readonly RuntimeFunction Rethrow = new("__gflat_eh_rethrow", "void", "i8*", NoReturn: true);
        public static readonly RuntimeFunction RethrowDebug = new("__gflat_eh_rethrow_debug", "void", "i8*", NoReturn: true);
        public static readonly RuntimeFunction Terminate = new("__gflat_eh_terminate", "void", "i8*, void (i8*)*", CanUnwind: false, NoReturn: true);
        public static readonly RuntimeFunction[] Functions = [Allocate, Free, Throw, ThrowDebug, Capture, Object, Release, Rethrow, RethrowDebug, Terminate];
    }

    private readonly HashSet<RuntimeFunction> _runtimeFunctions = new();

    private string NativeDeclarations => _runtimeFunctions.Count == 0 ? "" :
        string.Concat(_runtimeFunctions.OrderBy(f => f.Name, StringComparer.Ordinal)
            .Where(f => !llvmExternalDeclarations.ContainsKey(f.Name)).Select(f => f.Declaration + "\n")) +
        (WindowsEh ? "declare i32 @__CxxFrameHandler3(...)\n" :
            "declare i32 @__gxx_personality_v0(...)\ndeclare i8* @__cxa_begin_catch(i8*) nounwind\ndeclare void @__cxa_end_catch() nounwind\n");

    private void EmitRuntimeCall(RuntimeFunction function, string arguments, string? result = null,
        bool handleUnwind = true, string? funclet = null)
    {
        _runtimeFunctions.Add(function);
        EmitCallInstruction(function.ReturnType, "@" + function.Name, arguments, result,
            handleUnwind: function.CanUnwind && handleUnwind, funclet: funclet);
    }
}
