using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private int _staticStoragePhase;
    private bool _registerStaticFinalizer;
    private static string StaticLive(TypeChecker.StaticStorage storage) => "@" + storage.Symbol + "$live";
    private bool TryEmitStaticAddress(AstNode expression)
    {
        if (_typeChecker.GetStaticStorage(expression) is not { IsConst: false } field) return false;
        Push("@" + _typeChecker.GetStaticStorageInfo(field).Symbol);
        return true;
    }
    private bool TryEmitStaticRead(AstNode expression)
    {
        if (_typeChecker.GetStaticStorage(expression) is not { IsConst: false } field) return false;
        EmitStaticReadGuard(field);
        string address = "@" + _typeChecker.GetStaticStorageInfo(field).Symbol;
        string llvm = EmitType(field.Type), value = NewTemp();
        if (field.Type is ArrayTypeExpression)
            Emit($"    {value} = getelementptr {llvm}, {llvm}* {address}, i32 0, i32 0");
        else Emit($"    {value} = load {llvm}, {llvm}* {address}");
        PushManaged(expression, value);
        return true;
    }

    private void EmitStaticReadGuard(FieldDeclaration field)
    {
        // Zero initialization is a valid value only for default-initializable types.
        // An indirect call during module startup must not observe a null value for
        // a nonnullable pointer whose dynamic initializer has not run yet.
        if (_typeChecker.CanDefaultInitialize(field.Type) || _typeChecker.TryGetStaticInitializer(field, out _)) return;
        var storage = _typeChecker.GetStaticStorageInfo(field);
        string live = NewTemp(), ready = NewLabel("static_ready"), invalid = NewLabel("static_uninitialized");
        Emit($"    {live} = load i1, i1* {StaticLive(storage)}");
        Emit($"    br i1 {live}, label %{ready}, label %{invalid}");
        Emit(invalid + ":");
        EmitCallInstruction("void", "@llvm.trap", "", handleUnwind: false);
        Emit("    unreachable");
        Emit(ready + ":");
    }

    private void EmitStaticStorage()
    {
        var fields = _typeChecker.GetStaticStorage().Where(s => !s.Field.IsConst).ToArray();
        var initializers = _typeChecker.GetStaticInitializers();
        if (fields.Length == 0 && initializers.Count == 0) return;
        foreach (var storage in fields)
        {
            string initializer = _typeChecker.TryGetStaticInitializer(storage.Field, out var constant)
                ? EmitConstInitializer(constant!, storage.Field.Type)
                : EmitType(storage.Field.Type) + " " + GetDefaultValue(storage.Field.Type);
            EmitGlobal($"@{storage.Symbol} = internal global {initializer}");
            bool live = constant != null || storage.Field.Initializer == null && !NeedsCleanup(storage.Field.Type);
            EmitGlobal($"{StaticLive(storage)} = internal global i1 {(live ? "true" : "false")}");
        }
        if (initializers.Count == 0) return;
        _registerStaticFinalizer = fields.Any(s => NeedsCleanup(s.Field.Type));
        EmitGlobal("@llvm.global_ctors = appending global [1 x { i32, void ()*, i8* }] [{ i32, void ()*, i8* } { i32 65535, void ()* @gflat$$static_init, i8* null }]");
        // Register with the hosted runtime while it is live. COFF global_dtors
        // run after CRT teardown, too late for destructors that use stdio or free.
        if (_registerStaticFinalizer) ValidateStaticShutdownAbi();
        if (_registerStaticFinalizer && !_nativeExternDeclarations.ContainsKey("atexit"))
        {
            const string declaration = "declare i32 @atexit(void ()*)";
            _nativeExternDeclarations["atexit"] = declaration;
            EmitGlobal(declaration);
        }
        var previousClass = _currentClass; var previousStruct = _currentStruct;
        string previousNamespace = _currentNamespacePath;
        _currentClass = null; _currentStruct = null; _currentNamespacePath = "";
        try
        {
            _staticStoragePhase = 1;
            EmitLifetime("$static_init", initializers.ToList());
            if (_registerStaticFinalizer)
            {
                _staticStoragePhase = 2;
                EmitLifetime("$static_fini", fields.Reverse().Where(s => NeedsCleanup(s.Field.Type)).Select(s => (AstNode)s.Field).ToList());
            }
        }
        finally
        {
            _staticStoragePhase = 0;
            _currentClass = previousClass; _currentStruct = previousStruct; _currentNamespacePath = previousNamespace;
        }
        void EmitLifetime(string name, List<AstNode> body) => Visit(new MethodDeclaration(name,
            new NamedTypeExpression("void", null, 0), [], new BlockStatement(body, 0),
            TokenKind.Private, false, false, false, false, 0, isNoThrow: true));
    }

    private void EmitStaticFieldLifetime(FieldDeclaration field)
    {
        if (!_typeChecker.GetStaticStorage().Any(s => s.Field == field)) return;
        var storage = _typeChecker.GetStaticStorageInfo(field);
        string address = "@" + storage.Symbol;
        if (_staticStoragePhase == 2) { EmitStaticCleanup(storage); return; }
        EmitExpressionWithCleanup(() =>
        {
            EmitManagedReferences(field.Type, address, true);
            RegisterManagedCleanup(field.Type, address, onExceptionOnly: true);
            if (field.Initializer != null)
            {
                EmitValueForTarget(field.Initializer, field.Type);
                string value = EmitImplicitCast(Pop(), _typeChecker.GetType(field.Initializer), field.Type, field.Initializer);
                EmitManagedStore(field.Type, value, address);
            }
            Emit($"    store i1 true, i1* {StaticLive(storage)}");
        });
        // Only unwind cleanup belongs here. Successful initialization transfers
        // the lifetime to the module's shutdown routine.
        if (NeedsCleanup(field.Type))
            _deferScopes[^1].Add(new ExceptionOnlyCleanup(new StaticStorageCleanup(storage)));
    }

    private void EmitStaticLifetimePrologue()
    {
        if (_staticStoragePhase != 1 || !_registerStaticFinalizer) return;
        string registered = NewTemp();
        EmitCallInstruction("i32", "@atexit", "void ()* @gflat$$static_fini", registered);
        string failed = NewTemp(), fail = NewLabel("static_registration_failed"), ready = NewLabel("static_registration_ready");
        Emit($"    {failed} = icmp ne i32 {registered}, 0");
        Emit($"    br i1 {failed}, label %{fail}, label %{ready}");
        Emit(fail + ":"); EmitCallInstruction("void", "@llvm.trap", "", handleUnwind: false); Emit("    unreachable"); Emit(ready + ":");
    }

    private sealed class StaticStorageCleanup(TypeChecker.StaticStorage storage) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter) => emitter.EmitStaticCleanup(storage);
    }
    private void EmitStaticCleanup(TypeChecker.StaticStorage storage)
    {
        string live = NewTemp(), clean = NewLabel("static_destroy"), done = NewLabel("static_destroyed");
        Emit($"    {live} = load i1, i1* {StaticLive(storage)}");
        Emit($"    br i1 {live}, label %{clean}, label %{done}");
        Emit(clean + ":");
        Emit($"    store i1 false, i1* {StaticLive(storage)}");
        string address = "@" + storage.Symbol;
        if (_typeChecker.HasDestructor(storage.Field.Type)) EmitDestroy(storage.Field.Type, address);
        else EmitManagedReferences(storage.Field.Type, address, false);
        Emit($"    br label %{done}");
        Emit(done + ":");
    }
}
