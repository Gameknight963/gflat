using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private void EmitImplicitBaseConstruction(string derivedName, TypeChecker.ClassInfo baseInfo, string baseThis)
    {
        if (_typeChecker.GetImplicitBaseCall(derivedName) is not { } call)
        {
            EmitConstructorCall(GetConstructorMangledName(baseInfo.Name, null, baseInfo.Namespace),
                baseInfo.Constructors.FirstOrDefault(c => c.Parameters.Count == 0), $"%{baseInfo.Name}* {baseThis}");
            return;
        }
        EmitExpressionWithCleanup(() => WithArgumentOwnership(() =>
        {
            var arguments = new List<string> { $"%{baseInfo.Name}* {baseThis}" };
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                EmitArgument(call.Arguments[i], call.Constructor.Parameters[i].Type);
                arguments.Add($"{EmitParamType(call.Constructor.Parameters[i].Type)} {Pop()}");
            }
            TransferArguments();
            EmitConstructorCall(GetConstructorMangledName(baseInfo.Name, call.Constructor, baseInfo.Namespace), call.Constructor, string.Join(", ", arguments));
        }));
    }

    private int _exceptionCleanup;
    private readonly Dictionary<string, string> _constructionFields = new();
    private sealed class ExceptionOnlyCleanup(IDeferAction action) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter)
        {
            if (emitter._exceptionCleanup > 0) action.Execute(emitter);
        }
    }
    private sealed class AllocationCleanup(string address) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter)
            => emitter.Emit($"    call void @{emitter.RuntimeMemorySymbol("Free")}(i8* {address})");
    }

    private sealed class ExceptionAllocationCleanup(string address) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter)
            => emitter.EmitExceptionFree(address);
    }

    private void EmitExceptionFree(string address)
    {
        Emit($"    call void @__gflat_eh_free_exception(i8* {address}, void (i8*)* @{RuntimeMemorySymbol("Free")})");
    }

    private void BeginConstruction(string name, ConstructorDeclaration constructor)
    {
        _deferScopes.Add(new());
        _deferScopes[^1].AddRange(_parameterOwners);
        _parameterOwners.Clear();
        _constructionFields.Clear();
        _constructorLiveFlags.Clear();
        var constructing = new NamedTypeExpression(name, null, constructor.Line);
        if (_typeChecker.UsesCollector && HasManagedReferences(constructing))
            _deferScopes[^1].Add(new ExceptionOnlyCleanup(new ManagedReferenceCleanup(constructing, "%this")));
        var cls = _typeChecker.GetClass(name);
        var str = _typeChecker.GetStruct(name);
        foreach (var field in str?.FieldDeclarations ?? cls!.FieldDeclarations)
        {
            if (field.IsStatic) continue;
            if (!_typeChecker.HasDestructor(field.Type)) continue;
            string flag = NewTemp(), address = NewTemp();
            int index = str != null ? str.FieldIndex(field.Name) : cls!.FieldIndex(field.Name) + 1;
            Emit($"    {address} = getelementptr %{name}, %{name}* %this, i32 0, i32 {index}");
            Emit($"    {flag} = alloca i1");
            bool live = _typeChecker.CanDefaultInitialize(field.Type);
            Emit($"    store i1 {(live ? "true" : "false")}, i1* {flag}");
            _constructionFields[field.Name] = flag;
            _deferScopes[^1].Add(new ExceptionOnlyCleanup(new ConditionalTemporaryCleanup(field.Type, address, flag)));
        }
        foreach (var group in _typeChecker.ConstructorWrites(constructor).GroupBy(w => w.Value.Path))
        {
            if (!_constructionFields.TryGetValue(group.Key, out string? flag))
            {
                flag = NewTemp();
                Emit($"    {flag} = alloca i1");
                Emit($"    store i1 {(group.First().Value.Live ? "true" : "false")}, i1* {flag}");
            }
            foreach (var write in group) _constructorLiveFlags[write.Key] = flag;
        }
    }
    private void CompleteConstruction()
    {
        // The completed receiver belongs to the caller. Only parameters are cleaned here.
        if (!_hasTerminated) EmitDefersDownTo(_deferScopes.Count - 1);
        _deferScopes.RemoveAt(_deferScopes.Count - 1);
        _constructionFields.Clear();
        _constructorLiveFlags.Clear();
    }
}
