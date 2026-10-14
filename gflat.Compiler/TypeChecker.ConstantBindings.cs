using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private readonly Stack<Dictionary<string, ConstValue>> localConstants = new();

    private FieldDeclaration? ConstantField(string name)
    {
        int separator = name.LastIndexOf("::", StringComparison.Ordinal);
        if (separator >= 0) {
            string owner = name[..separator], member = name[(separator + 2)..];
            return GetClass(owner)?.FieldDeclarationsByName.GetValueOrDefault(member) ??
                GetStruct(owner)?.FieldDeclarationsByName.GetValueOrDefault(member) ?? ResolveNamespaceByName(owner)?.Fields.GetValueOrDefault(member);
        }
        return _currentClass?.FieldDeclarationsByName.GetValueOrDefault(name) ??
            _currentStruct?.FieldDeclarationsByName.GetValueOrDefault(name) ?? ResolveGlobalField(name);
    }

    private bool BoundConstant(string name, out ConstValue? value)
    {
        value = null;
        foreach (var frame in _scopes.Zip(localConstants))
            if (frame.First.ContainsKey(name)) return frame.Second.TryGetValue(name, out value);
        if (ConstantField(name) is { } field) return EvaluateDefaultConstant(field, out value);
        // Preserve the legacy inspection API outside any source query. Compiler
        // reads are bound to scopes or declarations, never this spelling cache.
        return SourceContext.Current == null && _constVariablesByName.TryGetValue(name, out value);
    }

    private bool BoundConstantIsReadOnly(string name)
    {
        foreach (var frame in _scopes.Zip(localConstants))
            if (frame.First.ContainsKey(name)) return frame.Second.ContainsKey(name);
        return ConstantField(name)?.IsConst == true;
    }
}
