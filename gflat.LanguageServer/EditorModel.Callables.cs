using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<Token, Symbol> boundCallableReferences = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Symbol[]> boundCallableFamilies = new(ReferenceEqualityComparer.Instance);

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
        foreach (var syntax in snapshot.Nodes)
        {
            if (syntax.Node is NewExpression { IsTargetTyped: true } creation && syntax.Span.Source?.Path is { } creationPath)
            {
                var creationToken = tokens[creationPath].FirstOrDefault(t => t.Start == syntax.Span.Start && t.Kind == TokenKind.New);
                string? creationOwner = TypeOwner(creation.Type, creationPath, syntax.Span.Start);
                var creationFamily = symbols.Where(s => s.Syntax.Node is ConstructorDeclaration && s.Owner == creationOwner && Accessible(s, creationPath, syntax.Span.Start)).ToArray();
                if (creation.Type is NamedTypeExpression creationType)
                {
                    var constructors = checker.GetClassByIdentity(creationType.Name)?.Constructors ?? checker.GetStructByIdentity(creationType.Name)?.Constructors;
                    if (constructors != null)
                        creationFamily = constructors.Select(ctor => SourceCallable(ctor) is { } sourceCtor
                            ? sourceCtor with { BoundParameters = ctor.Parameters } : null)
                            .OfType<Symbol>().Where(s => Accessible(s, creationPath, syntax.Span.Start)).ToArray();
                }
                if (creationToken != null && creationFamily.Length > 0)
                {
                    boundCallableFamilies[creationToken] = creationFamily;
                    if (checker.GetResolvedConstructor(creation) is { } creationConstructor && SourceCallable(creationConstructor) is { } creationSource)
                        boundCallableReferences[creationToken] = creationSource with { BoundParameters = creationConstructor.Parameters };
                }
            }
            CallExpression? call = syntax.Node as CallExpression;
            AstNode expression = call?.Callee ?? (syntax.Node is UnaryExpression { Operator: TokenKind.Ampersand } address ? address.Operand : syntax.Node);
            if (call != null && checker.GetExtensionDefinition(call) != null) continue;
            var target = (call != null ? checker.GetResolvedCall(call) : checker.GetFunctionAddressTarget(syntax.Node)) as MethodDeclaration;
            if (target == null || SourceCallable(target) is not { } source || source.Syntax.Node is not MethodDeclaration definition ||
                expression.Span.Source?.Path is not { } path || !tokens.TryGetValue(path, out var fileTokens)) continue;
            string? name = expression switch { IdentifierExpression id => id.Name, NamespaceAccessExpression access => access.Member, MemberAccessExpression access => access.Member, _ => null };
            if (name == null) continue;
            int memberStart = expression switch { NamespaceAccessExpression access => access.Left.Span.Start + access.Left.Span.Length, MemberAccessExpression access => access.Object.Span.Start + access.Object.Span.Length, _ => expression.Span.Start };
            var token = fileTokens.FirstOrDefault(token => token.Kind == TokenKind.Identifier && token.Text == name && token.Start >= memberStart && token.End < expression.Span.Start + expression.Span.Length);
            if (token == null) continue;
            if (!bound.TryGetValue(target, out var selected)) bound[target] = selected = BindCallable(source, target);
            boundCallableReferences[token] = selected;
            if (ReferenceEquals(target, definition)) continue;
            IEnumerable<MethodDeclaration> methods = [target];
            if (checker.TryGetMethodOwner(target, out var owner, out _))
                methods = checker.GetClassByIdentity(owner)?.AllMethods ?? checker.GetStructByIdentity(owner)?.AllMethods ?? [target];
            var family = methods.Where(method => !method.IsGeneric && SourceCallable(method) is { } original &&
                    original.Name == source.Name && original.Owner == source.Owner)
                .Select(method => ReferenceEquals(method, target) ? selected : BindCallable(SourceCallable(method)!, method)).ToArray();
            boundCallableFamilies[token] = family.Contains(selected) ? family : [selected];
        }
    }

    private Symbol BindCallable(Symbol source, MethodDeclaration target)
    {
        if (ReferenceEquals(source.Syntax.Node, target) || snapshot.Checker is not { } checker) return source;
        if (checker.GetPrimitiveMethodSource(target) != null)
            return source with { Type = target.ReturnType, BoundParameters = target.Parameters.Skip(1).ToArray(), BoundMethod = target };
        var definition = (MethodDeclaration)source.Syntax.Node;
        var genericNames = DisplayGenericNames(source);
        var bindings = new Dictionary<string, TypeExpression>();
        string? owner = source.Owner;
        if (checker.TryGetMethodOwner(target, out var identity, out _))
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
