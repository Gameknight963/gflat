using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
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
            => emitter.Emit($"    call void @gflat$Memory$Free(i8* {address})");
    }

    private void BeginConstruction(string name, ConstructorDeclaration constructor)
    {
        _deferScopes.Add(new());
        _deferScopes[^1].AddRange(_parameterOwners);
        _parameterOwners.Clear();
        _constructionFields.Clear();
        _constructorLiveFlags.Clear();
        var cls = _typeChecker.GetClass(name);
        var str = _typeChecker.GetStruct(name);
        foreach (var field in str?.FieldDeclarations ?? cls!.FieldDeclarations)
        {
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
