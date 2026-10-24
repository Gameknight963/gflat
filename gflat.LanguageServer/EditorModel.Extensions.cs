using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private bool IsCallableMember(Symbol symbol)
    {
        if (symbol.Syntax.Node is MethodDeclaration || symbol.Type is FunctionPointerTypeExpression) return true;
        if (symbol.Type == null) return false;
        try { return snapshot.Checker?.ResolveAlias(symbol.Type) is FunctionPointerTypeExpression; }
        catch (gflat.CompileExceptions.TypeCheckException) { return false; }
    }

    private Symbol? ExtensionCallSymbol(CallExpression call)
    {
        if (snapshot.Checker is not { } checker || checker.GetExtensionDefinition(call) is not { } definition ||
            !declarations.TryGetValue(definition, out var symbol) || checker.GetResolvedCall(call) is not MethodDeclaration target)
            return null;
        var bindings = definition.GenericParameters.Select(parameter => parameter.Name)
            .Zip(checker.GetExtensionCall(call)!.TypeArguments).ToDictionary(pair => pair.First, pair => pair.Second);
        var cloner = new AstCloner(bindings);
        var parameters = definition.Parameters.Skip(1).Select(parameter => new Parameter(parameter.Name,
            cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(parameter) ?? parameter.Type), parameter.Line,
            parameter.IsConst, parameter.DefaultValue) { Span = parameter.Span }).ToArray();
        return symbol with {
            Type = cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(definition) ?? definition.ReturnType),
            BoundParameters = parameters,
            GenericBindings = bindings,
            BoundMethod = target,
            BoundExtension = true
        };
    }

    private IEnumerable<Symbol> ExtensionMembers(string path, int offset, AstNode? receiver, TypeExpression? receiverType, string? memberName = null)
    {
        if (snapshot.Checker is not { } checker || receiverType == null) return [];
        string ns = ScopeAt(path, offset).Namespace;
        var imports = snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? [];
        bool storage = receiver != null && ExtensionReceiverStorage(receiver, path, offset);
        bool readOnly = receiverType.IsReadOnlyValue || receiver != null && ExtensionReceiverReadOnly(receiver, path, offset);
        var storageType = receiver == null ? null : checker.GetExtensionReceiverStorageType(receiver);
        return checker.GetVisibleExtensions(ns, imports, receiverType, storage, readOnly, memberName: memberName, receiverStorageType: storageType)
            .GroupBy(candidate => candidate.Definition.Name)
            .SelectMany(group => group.Any(candidate => candidate.Adaptation == TypeChecker.ExtensionReceiverAdaptation.Exact)
                ? group.Where(candidate => candidate.Adaptation == TypeChecker.ExtensionReceiverAdaptation.Exact) : group)
            .Select(candidate => ExtensionCandidateSymbol(candidate)).OfType<Symbol>()
            .Where(symbol => Accessible(symbol, path, offset));
    }

    private Symbol? ExtensionCandidateSymbol(TypeChecker.ExtensionMethodCandidate candidate)
    {
        if (!declarations.TryGetValue(candidate.Definition, out var symbol)) return null;
        var cloner = new AstCloner(candidate.GenericBindings.ToDictionary(pair => pair.Key, pair => pair.Value));
        var parameters = candidate.Definition.Parameters.Skip(1).Select(parameter => new Parameter(parameter.Name,
            cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(parameter) ?? parameter.Type), parameter.Line,
            parameter.IsConst, parameter.DefaultValue) { Span = parameter.Span }).ToArray();
        return symbol with {
            Type = cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(candidate.Definition) ?? candidate.Definition.ReturnType),
            BoundParameters = parameters,
            GenericBindings = candidate.GenericBindings,
            BoundExtension = true
        };
    }

    private bool ExtensionReceiverStorage(AstNode receiver, string path, int offset)
    {
        if (receiver is IdentifierExpression identifier)
            return snapshot.Checker?.CanBorrowExtensionReceiver(receiver) == true ||
                Visible(path, offset).FirstOrDefault(symbol => symbol.Name == identifier.Name)?.Syntax.Node is
                VariableDeclaration { IsConst: false } or Parameter { IsConst: false } or FieldDeclaration { IsConst: false };
        if (snapshot.Checker is { } checker && TypeChecker.TypeName(checker.GetType(receiver)) != "<error>")
            return checker.CanBorrowExtensionReceiver(receiver);
        return false;
    }

    private bool ExtensionReceiverReadOnly(AstNode receiver, string path, int offset)
    {
        if (receiver is IdentifierExpression identifier &&
            Visible(path, offset).FirstOrDefault(symbol => symbol.Name == identifier.Name)?.Syntax.Node is
                VariableDeclaration { IsConst: true } or Parameter { IsConst: true } or FieldDeclaration { IsConst: true })
            return true;
        return snapshot.Checker?.IsExtensionReceiverReadOnly(receiver) == true;
    }
}
