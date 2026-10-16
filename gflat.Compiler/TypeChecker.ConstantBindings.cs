using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private readonly Stack<Dictionary<string, (ConstValue Value, VariableDeclaration Declaration)>> localConstants = new();
    private readonly Dictionary<AstNode, AstNode> constantDeclarationReads = new();

    public AstNode? GetConstantDeclaration(AstNode expression)
        => GetStaticStorage(expression) is { IsConst: true } field ? field : constantDeclarationReads.GetValueOrDefault(expression);

    private void BindConstantDeclaration(IdentifierExpression expression)
    {
        foreach (var frame in _scopes.Zip(localConstants))
            if (frame.First.ContainsKey(expression.Name))
            {
                if (frame.Second.TryGetValue(expression.Name, out var binding))
                    constantDeclarationReads[expression] = binding.Declaration;
                return;
            }
        if (ConstantField(expression.Name) is { IsConst: true } field) constantDeclarationReads[expression] = field;
    }

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
            if (frame.First.ContainsKey(name))
            {
                if (!frame.Second.TryGetValue(name, out var binding)) return false;
                value = binding.Value;
                return true;
            }
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
