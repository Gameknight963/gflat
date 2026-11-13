using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private static string LocalGuard(TypeChecker.StaticStorage storage) => "@" + storage.Symbol + "$guard";
    private bool LocalNeedsInitialization(TypeChecker.StaticStorage storage) =>
        !_typeChecker.TryGetStaticInitializer(storage.Field, out _) &&
        (storage.Field.Initializer != null || NeedsCleanup(storage.Field.Type));

    private void EmitStaticLocalSupport(TypeChecker.StaticStorage[] locals)
    {
        if (locals.Any(LocalNeedsInitialization))
            EmitGlobal("@gflat$$static_thread = internal thread_local global i8 0");
        foreach (var storage in locals)
        {
            if (LocalNeedsInitialization(storage)) EmitGlobal($"{LocalGuard(storage)} = internal global i8* null");
            if (!NeedsCleanup(storage.Field.Type)) continue;
            ValidateStaticShutdownAbi();
            if (!_nativeExternDeclarations.ContainsKey("atexit"))
            {
                const string declaration = "declare i32 @atexit(void ()*)";
                _nativeExternDeclarations["atexit"] = declaration;
                EmitGlobal(declaration);
            }
            _staticStoragePhase = 2;
            try
            {
                Visit(new MethodDeclaration(storage.CleanupSymbol!["gflat$".Length..],
                    new NamedTypeExpression("void", null, 0), [], new BlockStatement([storage.Field], 0),
                    TokenKind.Private, false, false, false, false, 0, isNoThrow: true));
            }
            finally { _staticStoragePhase = 0; }
        }
    }

    private void EmitStaticLocal(TypeChecker.StaticStorage storage)
    {
        string address = "@" + storage.Symbol;
        if (!LocalNeedsInitialization(storage)) { _locals[storage.Field.Name] = address; return; }
        string ready = NewLabel("local_static_ready"), check = NewLabel("local_static_check"),
            acquire = NewLabel("local_static_acquire"), initialize = NewLabel("local_static_initialize"),
            wait = NewLabel("local_static_wait"), recursive = NewLabel("local_static_recursive");
        string guard = LocalGuard(storage), completed = $"inttoptr (i{TargetInfo.Default.PointerBits} 1 to i8*)";
        int align = TargetInfo.Default.PointerBytes;
        Emit($"    br label %{check}");
        Emit(check + ":");
        string owner = NewTemp(), done = NewTemp();
        Emit($"    {owner} = load atomic i8*, i8** {guard} acquire, align {align}");
        Emit($"    {done} = icmp eq i8* {owner}, {completed}");
        Emit($"    br i1 {done}, label %{ready}, label %{acquire}");
        Emit(acquire + ":");
        string exchange = NewTemp(), acquired = NewTemp();
        Emit($"    {exchange} = cmpxchg i8** {guard}, i8* null, i8* @gflat$$static_thread acquire monotonic");
        Emit($"    {acquired} = extractvalue {{ i8*, i1 }} {exchange}, 1");
        Emit($"    br i1 {acquired}, label %{initialize}, label %{wait}");
        Emit(wait + ":");
        string previous = NewTemp(), reentered = NewTemp();
        Emit($"    {previous} = extractvalue {{ i8*, i1 }} {exchange}, 0");
        Emit($"    {reentered} = icmp eq i8* {previous}, @gflat$$static_thread");
        Emit($"    br i1 {reentered}, label %{recursive}, label %{check}");
        Emit(recursive + ":");
        EmitCallInstruction("void", "@llvm.trap", "", handleUnwind: false);
        Emit("    unreachable");
        Emit(initialize + ":");
        _deferScopes.Add([new ExceptionOnlyCleanup(new StaticLocalGuardAbort(guard))]);
        try
        {
            EmitStaticFieldLifetime(storage.Field);
            if (NeedsCleanup(storage.Field.Type))
            {
                string registered = NewTemp();
                EmitCallInstruction("i32", "@atexit", $"void ()* @{storage.CleanupSymbol}", registered, handleUnwind: false);
                string failed = NewTemp(), failure = NewLabel("local_static_registration_failed"), success = NewLabel("local_static_registered");
                Emit($"    {failed} = icmp ne i32 {registered}, 0");
                Emit($"    br i1 {failed}, label %{failure}, label %{success}");
                Emit(failure + ":");
                EmitCallInstruction("void", "@llvm.trap", "", handleUnwind: false);
                Emit("    unreachable");
                Emit(success + ":");
            }
            Emit($"    store atomic i8* {completed}, i8** {guard} release, align {align}");
        }
        finally { _deferScopes.RemoveAt(_deferScopes.Count - 1); }
        Emit($"    br label %{ready}");
        Emit(ready + ":");
        _locals[storage.Field.Name] = address;
    }

    private sealed class StaticLocalGuardAbort(string guard) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter) => emitter.Emit(
            $"    store atomic i8* null, i8** {guard} release, align {TargetInfo.Default.PointerBytes}");
    }
}
