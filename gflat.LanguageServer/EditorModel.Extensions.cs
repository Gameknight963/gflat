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
        var cloner = CreateDisplayCloner(bindings);
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
        while (receiverType is ReferenceTypeExpression reference) receiverType = reference.Inner;
        string ns = ScopeAt(path, offset).Namespace;
        var imports = snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? [];
        bool storage = receiver != null && ExtensionReceiverStorage(receiver, path, offset);
        bool readOnly = receiverType.IsReadOnlyValue || receiver != null && ExtensionReceiverReadOnly(receiver, path, offset);
        var storageType = receiver == null ? null : checker.GetExtensionReceiverStorageType(receiver);
        var sourceReceiver = receiverType;
        if (OpenExtensionParameterBinding(path, offset, receiverType) is { } binding)
        {
            var cloner = CreateDisplayCloner(binding);
            receiverType = cloner.CloneType(receiverType);
            if (storageType != null) storageType = cloner.CloneType(storageType);
        }
        receiverType = SourceNominalExtensionReceiver(receiverType, sourceReceiver, path, offset);
        return checker.GetVisibleExtensions(ns, imports, receiverType, storage, readOnly, memberName: memberName, receiverStorageType: storageType)
            .GroupBy(candidate => candidate.Definition.Name)
            .SelectMany(group => group.Any(candidate => candidate.Adaptation == TypeChecker.ExtensionReceiverAdaptation.Exact)
                ? group.Where(candidate => candidate.Adaptation == TypeChecker.ExtensionReceiverAdaptation.Exact) : group)
            .Select(candidate => ExtensionCandidateSymbol(candidate)).OfType<Symbol>()
            .Where(symbol => Accessible(symbol, path, offset));
    }

    private TypeExpression SourceNominalExtensionReceiver(TypeExpression receiver, TypeExpression source, string path, int offset)
    {
        if (receiver is not NamedTypeExpression { Namespace: null, TypeArguments.Count: > 0 } named ||
            source is not NamedTypeExpression original || snapshot.Checker!.IsResolvedTypeIdentity(named) ||
            !sourceTypeNames.TryGetValue(original.NameSpan, out var captured) || !ReferenceEquals(original, captured)) return receiver;
        var lexical = Visible(path, offset, original.Name).FirstOrDefault(symbol => symbol.Name == original.Name &&
            (symbol.IsTypeParameter || IsType(symbol.Syntax.Node) || symbol.Syntax.Node is AliasDeclaration));
        string? identity = lexical?.IsTypeParameter == false ? lexical.Syntax.Node switch {
            ClassDeclaration declaration => declaration.Name, StructDeclaration declaration => declaration.Name,
            InterfaceDeclaration declaration => declaration.Name, _ => null
        } : null;
        if (identity == null || identity == named.Name) return receiver;
        return new NamedTypeExpression(identity, null, named.Line, named.TypeArguments.ToList()) {
            Span = named.Span, NameSpan = named.NameSpan
        }.WithQualifiersFrom(named);
    }

    private Dictionary<string, TypeExpression>? OpenExtensionParameterBinding(string path, int offset, TypeExpression type)
    {
        while (type is ReferenceTypeExpression or PointerTypeExpression or ManagedTypeExpression)
            type = type switch { ReferenceTypeExpression reference => reference.Inner,
                PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner, _ => type };
        if (type is NamedTypeExpression { TypeArguments.Count: > 0 } applied)
        {
            var bindings = new Dictionary<string, TypeExpression>();
            foreach (var argument in applied.TypeArguments)
                if (OpenExtensionParameterBinding(path, offset, argument) is { } argumentBindings)
                    foreach (var binding in argumentBindings) bindings[binding.Key] = binding.Value;
            return bindings.Count == 0 ? null : bindings;
        }
        if (type is not NamedTypeExpression { Namespace: null, TypeArguments.Count: 0 } named ||
            snapshot.Checker!.IsResolvedTypeIdentity(named) ||
            Visible(path, offset, named.Name).FirstOrDefault(symbol => symbol.IsTypeParameter) is not { } parameter)
            return null;
        TypeExpression? constraint = null;
        if (parameter.TypeConstraint != null)
        {
            var span = parameter.TypeConstraint.Span;
            string constraintPath = span.Source?.Path ?? path;
            int constraintOffset = span.Source == null ? parameter.NameSpan.Start : span.Start;
            string? owner = TypeOwner(parameter.TypeConstraint, constraintPath, constraintOffset);
            string? identity = symbols.FirstOrDefault(symbol => symbol.Qualified == owner)?.Syntax.Node switch {
                InterfaceDeclaration contract => contract.Name,
                _ => null
            };
            if (identity == null) return null;
            constraint = new NamedTypeExpression(identity, null, parameter.TypeConstraint.Line);
        }
        var view = snapshot.Checker!.CreateOpenGenericParameterView(parameter.Syntax.Node, named.Name, constraint);
        return view == null ? null : new() { [named.Name] = view };
    }

    private Symbol? ExtensionCandidateSymbol(TypeChecker.ExtensionMethodCandidate candidate)
    {
        if (!declarations.TryGetValue(candidate.Definition, out var symbol)) return null;
        var cloner = CreateDisplayCloner(candidate.GenericBindings);
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
                Visible(path, offset, identifier.Name).FirstOrDefault(symbol => symbol.Name == identifier.Name)?.Syntax.Node is
                VariableDeclaration { IsConst: false } or Parameter { IsConst: false } or FieldDeclaration { IsConst: false };
        if (snapshot.Checker is { } checker && TypeChecker.TypeName(checker.GetType(receiver)) != "<error>")
            return checker.CanBorrowExtensionReceiver(receiver);
        return false;
    }

    private bool ExtensionReceiverReadOnly(AstNode receiver, string path, int offset)
    {
        if (receiver is IdentifierExpression identifier &&
            Visible(path, offset, identifier.Name).FirstOrDefault(symbol => symbol.Name == identifier.Name)?.Syntax.Node is
                VariableDeclaration { IsConst: true } or Parameter { IsConst: true } or FieldDeclaration { IsConst: true })
            return true;
        return snapshot.Checker?.IsExtensionReceiverReadOnly(receiver) == true;
    }
}
