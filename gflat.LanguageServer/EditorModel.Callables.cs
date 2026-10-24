using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<Token, Symbol> boundCallableReferences = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Symbol[]> boundCallableFamilies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Token> constructorArgumentLists = new(ReferenceEqualityComparer.Instance);

    private static int CallableNameBeforeParenthesis(Token[] tokens, int parenthesis)
    {
        int index = parenthesis - 1;
        if (index >= 0 && tokens[index].Kind is TokenKind.Greater or TokenKind.GreaterGreater)
        {
            int depth = tokens[index].Kind == TokenKind.GreaterGreater ? 2 : 1;
            while (--index >= 0)
            {
                if (tokens[index].Kind == TokenKind.Greater) depth++;
                else if (tokens[index].Kind == TokenKind.GreaterGreater) depth += 2;
                else if (tokens[index].Kind == TokenKind.Less && --depth == 0) { index--; break; }
            }
        }
        return index >= 0 && tokens[index].Kind is TokenKind.Identifier or TokenKind.Base or TokenKind.New ? index : -1;
    }

    private Symbol? SourceCallable(AstNode target) => target is MethodDeclaration method && snapshot.Checker?.GetPrimitiveMethodSource(method) is { } original
        ? declarations.GetValueOrDefault(original) : declarations.GetValueOrDefault(target) ??
        sourceCallableDeclarations.GetValueOrDefault((target.GetType(), target.Span));

    private void IndexBoundCallables()
    {
        if (snapshot.Checker is not { } checker) return;
        var bound = new Dictionary<MethodDeclaration, Symbol>(ReferenceEqualityComparer.Instance);
        // Checking rewrites creation.Type and may reuse a declaration's type span.
        // The captured child span still identifies the type actually written here.
        var writtenCreationTypes = new Dictionary<NewExpression, SourceSpan>(ReferenceEqualityComparer.Instance);
        foreach (var syntax in snapshot.Nodes)
            if (syntax.Node is TypeExpression && syntax.Parent?.Node is NewExpression creation &&
                (!writtenCreationTypes.TryGetValue(creation, out var previous) || syntax.Span.Start < previous.Start))
                writtenCreationTypes[creation] = syntax.Span;
        foreach (var syntax in snapshot.Nodes)
        {
            if (syntax.Node is NewExpression creation && syntax.Span.Source?.Path is { } creationPath)
            {
                var creationToken = tokens[creationPath].FirstOrDefault(t => t.Start == syntax.Span.Start && t.Kind == TokenKind.New);
                Symbol[] creationFamily;
                if (creation.Type is NamedTypeExpression creationType &&
                    (checker.GetClassByIdentity(creationType.Name)?.Constructors ?? checker.GetStructByIdentity(creationType.Name)?.Constructors) is { } constructors)
                {
                    creationFamily = constructors.Select(ctor => SourceCallable(ctor) is { } sourceCtor
                        ? sourceCtor with { Owner = checker.DisplayName(creationType.Name), BoundParameters = ctor.Parameters } : null)
                        .OfType<Symbol>().Where(s => Accessible(s, creationPath, syntax.Span.Start)).ToArray();
                }
                else
                {
                    // Recover source names only when checked constructors are unavailable.
                    string? creationOwner = TypeOwner(creation.Type, creationPath, syntax.Span.Start);
                    creationFamily = symbols.Where(s => s.Syntax.Node is ConstructorDeclaration && s.Owner == creationOwner && Accessible(s, creationPath, syntax.Span.Start)).ToArray();
                }
                if (creationToken != null && creationFamily.Length > 0)
                {
                    boundCallableFamilies[creationToken] = creationFamily;
                    if (writtenCreationTypes.TryGetValue(creation, out var writtenType) &&
                        tokens[creationPath].FirstOrDefault(t => t.Kind == TokenKind.OpenParen &&
                            t.Start >= writtenType.Start + writtenType.Length && t.Start < syntax.Span.Start + syntax.Span.Length) is { } arguments)
                        constructorArgumentLists[arguments] = creationToken;
                    if (checker.GetResolvedConstructor(creation) is { } creationConstructor && SourceCallable(creationConstructor) is { } creationSource)
                        boundCallableReferences[creationToken] = creationSource with {
                            Owner = creation.Type is NamedTypeExpression named ? checker.DisplayName(named.Name) : creationSource.Owner,
                            BoundParameters = creationConstructor.Parameters
                        };
                }
            }
            CallExpression? call = syntax.Node as CallExpression;
            AstNode expression = call?.Callee ?? (syntax.Node is UnaryExpression { Operator: TokenKind.Ampersand } address ? address.Operand : syntax.Node);
            var extension = call == null ? null : ExtensionCallSymbol(call);
            string? interfaceOwner = null;
            var target = (call != null ? checker.GetResolvedCall(call) : checker.GetFunctionAddressTarget(syntax.Node)) as MethodDeclaration;
            // Interface dispatch has its own checker binding. Its method carries
            // the closed signature even though GetResolvedCall returns null.
            if (call != null && checker.TryGetInterfaceCall(call, out var interfaceCall))
            {
                target = interfaceCall.Method;
                interfaceOwner = interfaceCall.Interface.Name;
            }
            if (target == null || SourceCallable(target) is not { } source || source.Syntax.Node is not MethodDeclaration definition ||
                expression.Span.Source?.Path is not { } path || !tokens.TryGetValue(path, out var fileTokens)) continue;
            string? name = expression switch { IdentifierExpression id => id.Name, NamespaceAccessExpression access => access.Member, MemberAccessExpression access => access.Member, _ => null };
            if (name == null) continue;
            int memberStart = expression switch { NamespaceAccessExpression access => access.Left.Span.Start + access.Left.Span.Length, MemberAccessExpression access => access.Object.Span.Start + access.Object.Span.Length, _ => expression.Span.Start };
            var token = fileTokens.FirstOrDefault(token => token.Kind == TokenKind.Identifier && token.Text == name && token.Start >= memberStart && token.End < expression.Span.Start + expression.Span.Length);
            if (token == null) continue;
            Symbol selected;
            if (extension != null) selected = extension;
            else if (!bound.TryGetValue(target, out selected!)) bound[target] = selected = BindCallable(source, target, interfaceOwner);
            boundCallableReferences[token] = selected;
            boundCallableFamilies[token] = BoundCallableFamily(source, selected, target, path, token.Start);
        }
    }

    private Symbol[] BoundCallableFamily(Symbol source, Symbol selected, MethodDeclaration target, string path, int offset)
    {
        IEnumerable<MethodDeclaration> ownerMethods = [];
        if (snapshot.Checker is { } checker && checker.TryGetMethodOwner(target, out var owner, out _))
            ownerMethods = checker.GetClassByIdentity(owner)?.AllMethods ?? checker.GetStructByIdentity(owner)?.AllMethods ?? [];
        var closedOwners = ownerMethods.Select(method => (Method: method, Source: SourceCallable(method)))
            .Where(pair => pair.Source != null).ToArray();
        var primitiveImplementation = Parents(source.Syntax).FirstOrDefault(parent => parent.Node is PrimitiveImplementationDeclaration)?.Node;
        // Present the declared family, including generic siblings that were never
        // instantiated. The selected callable alone has argument-derived bindings.
        // Looking up existing closed owner declarations also substitutes owner type
        // parameters without checking a sibling's body or changing checker state.
        var family = SymbolsNamed(source.Name).Where(candidate => candidate.Syntax.Node is MethodDeclaration method &&
                (!selected.BoundExtension || method.Parameters.FirstOrDefault()?.IsExtensionReceiver == true) &&
                candidate.Namespace == source.Namespace && candidate.Owner == source.Owner && candidate.Scope == source.Scope &&
                ReferenceEquals(primitiveImplementation, Parents(candidate.Syntax).FirstOrDefault(parent => parent.Node is PrimitiveImplementationDeclaration)?.Node) &&
                Accessible(candidate, path, offset))
            .DistinctBy(candidate => candidate.Syntax).Select(candidate =>
            {
                if (candidate.Syntax == selected.Syntax) return selected;
                var definition = (MethodDeclaration)candidate.Syntax.Node;
                var method = closedOwners.FirstOrDefault(pair => pair.Source!.Syntax == candidate.Syntax &&
                    pair.Method.IsGeneric == definition.IsGeneric).Method;
                var sibling = method == null ? candidate : BindCallable(candidate, method);
                return selected.BoundExtension ? sibling with {
                    BoundParameters = sibling.Parameters!.Skip(1).ToArray(), BoundExtension = true
                } : sibling;
            }).ToArray();
        return family.Any(candidate => candidate.Syntax == selected.Syntax) ? family : [selected];
    }

    private Symbol BindInstanceMember(Symbol source, TypeExpression? receiver)
    {
        if (snapshot.Checker is not { } checker || source.Syntax.Node is not MethodDeclaration) return source;
        while (receiver is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
            receiver = receiver switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner,
                ReferenceTypeExpression reference => reference.Inner, _ => receiver };
        if (receiver is not NamedTypeExpression named) return source;
        if (checker.GetInterface(named.Name) is { } contract && contract.MethodsByName.TryGetValue(source.Name, out var method))
            return BindCallable(source, method, contract.Name);
        var methods = checker.GetStructByIdentity(named.Name)?.AllMethods ?? checker.GetClassByIdentity(named.Name)?.AllMethods;
        var target = methods?.FirstOrDefault(candidate => SourceCallable(candidate)?.Syntax == source.Syntax);
        return target == null ? source : BindCallable(source, target);
    }

    private Symbol BindCallable(Symbol source, MethodDeclaration target, string? interfaceOwner = null)
    {
        if (ReferenceEquals(source.Syntax.Node, target) || snapshot.Checker is not { } checker) return source;
        if (checker.GetPrimitiveMethodSource(target) != null)
            return source with { Type = target.ReturnType, BoundParameters = target.Parameters.Skip(1).ToArray(), BoundMethod = target };
        var definition = (MethodDeclaration)source.Syntax.Node;
        var genericNames = DisplayGenericNames(source);
        var bindings = new Dictionary<string, TypeExpression>();
        string? owner = source.Owner;
        string? identity = interfaceOwner;
        if (identity != null || checker.TryGetMethodOwner(target, out identity, out _))
        {
            owner = checker.DisplayName(identity);
            BindDisplayOwnerArguments(source, identity, bindings, definition.GenericParameters.Select(parameter => parameter.Name).ToHashSet());
        }
        Infer(snapshot.DeclaredTypes.GetValueOrDefault(definition) ?? definition.ReturnType, target.ReturnType);
        foreach (var (original, concrete) in definition.Parameters.Zip(target.Parameters))
            Infer(snapshot.DeclaredTypes.GetValueOrDefault(original) ?? original.Type, concrete.Type);
        // Unused method parameters have no signature evidence. Retain their
        // source names rather than guess from caller aliases or mangled names.
        var cloner = new AstCloner(bindings);
        var parametersBound = definition.Parameters.Select(parameter => new Parameter(parameter.Name,
            cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(parameter) ?? parameter.Type), parameter.Line,
            parameter.IsConst, parameter.DefaultValue) { Span = parameter.Span }).ToArray();
        return source with {
            Owner = owner,
            Type = cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(definition) ?? definition.ReturnType),
            BoundParameters = parametersBound,
            GenericBindings = bindings,
            BoundMethod = target
        };

        void Infer(TypeExpression pattern, TypeExpression concrete) => InferDisplayBindings(pattern, concrete, genericNames, bindings);
    }
}
