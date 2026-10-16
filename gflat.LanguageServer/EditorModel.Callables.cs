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
        return index >= 0 && tokens[index].Kind is TokenKind.Identifier or TokenKind.Base ? index : -1;
    }

    private Symbol? SourceCallable(AstNode target) => declarations.GetValueOrDefault(target) ??
        sourceCallableDeclarations.GetValueOrDefault((target.GetType(), target.Span));

    private void IndexBoundCallables()
    {
        if (snapshot.Checker is not { } checker) return;
        var bound = new Dictionary<MethodDeclaration, Symbol>(ReferenceEqualityComparer.Instance);
        foreach (var syntax in snapshot.Nodes)
        {
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
        var definition = (MethodDeclaration)source.Syntax.Node;
        var genericNames = new[] { source.Syntax }.Concat(Parents(source.Syntax)).SelectMany(syntax => syntax.Node switch {
            MethodDeclaration method => method.GenericParameters,
            ClassDeclaration type => type.GenericParameters,
            StructDeclaration type => type.GenericParameters,
            _ => []
        }).Select(parameter => parameter.Name).ToHashSet();
        var bindings = new Dictionary<string, TypeExpression>();
        string? owner = source.Owner;
        if (checker.TryGetMethodOwner(target, out var identity, out _))
        {
            owner = checker.DisplayName(identity);
            if (checker.GetGenericTypeOrigin(identity) is { } origin)
            {
                var parameters = Parents(source.Syntax).Select(syntax => syntax.Node switch {
                    ClassDeclaration type => type.GenericParameters,
                    StructDeclaration type => type.GenericParameters,
                    _ => null
                }).FirstOrDefault(parameters => parameters?.Count > 0);
                if (parameters?.Count == origin.TypeArguments.Count)
                    foreach (var (parameter, argument) in parameters.Zip(origin.TypeArguments))
                        if (!definition.GenericParameters.Any(methodParameter => methodParameter.Name == parameter.Name))
                            bindings[parameter.Name] = argument;
            }
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

        void Infer(TypeExpression pattern, TypeExpression concrete)
        {
            if (pattern is NamedTypeExpression { Namespace: null, TypeArguments.Count: 0 } parameter &&
                genericNames.Contains(parameter.Name) && !checker.IsResolvedTypeIdentity(parameter))
            { bindings.TryAdd(parameter.Name, concrete); return; }
            switch (pattern, concrete)
            {
                case (PointerTypeExpression a, PointerTypeExpression b): Infer(a.Inner, b.Inner); break;
                case (ManagedTypeExpression a, ManagedTypeExpression b): Infer(a.Inner, b.Inner); break;
                case (ReferenceTypeExpression a, ReferenceTypeExpression b): Infer(a.Inner, b.Inner); break;
                case (ArrayTypeExpression a, ArrayTypeExpression b): Infer(a.ElementType, b.ElementType); break;
                case (FunctionPointerTypeExpression a, FunctionPointerTypeExpression b):
                    Infer(a.ReturnType, b.ReturnType);
                    foreach (var (left, right) in a.ParameterTypes.Zip(b.ParameterTypes)) Infer(left, right);
                    break;
                default:
                    var arguments = pattern switch { NamedTypeExpression named => named.TypeArguments, NestedTypeExpression nested => nested.TypeArguments, _ => null };
                    var actual = concrete is NamedTypeExpression namedConcrete ? checker.GetGenericTypeOrigin(namedConcrete.Name)?.TypeArguments ?? namedConcrete.TypeArguments : null;
                    if (arguments != null && actual != null && arguments.Count == actual.Count)
                        foreach (var (left, right) in arguments.Zip(actual)) Infer(left, right);
                    break;
            }
        }
    }
}
