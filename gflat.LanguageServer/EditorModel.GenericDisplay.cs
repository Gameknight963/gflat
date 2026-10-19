using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private HashSet<string> DisplayGenericNames(Symbol source) => new[] { source.Syntax }.Concat(Parents(source.Syntax)).SelectMany(syntax => syntax.Node switch {
        MethodDeclaration method => method.GenericParameters,
        ClassDeclaration type => type.GenericParameters,
        StructDeclaration type => type.GenericParameters,
        InterfaceDeclaration type => type.GenericParameters,
        _ => []
    }).Select(parameter => parameter.Name).ToHashSet();

    private void BindDisplayOwnerArguments(Symbol source, string identity, Dictionary<string, TypeExpression> bindings, ISet<string>? shadowed = null)
    {
        if (snapshot.Checker?.GetGenericTypeOrigin(identity) is not { } origin) return;
        var parameters = Parents(source.Syntax).Select(syntax => syntax.Node switch {
            ClassDeclaration type => type.GenericParameters,
            StructDeclaration type => type.GenericParameters,
            InterfaceDeclaration type => type.GenericParameters,
            _ => null
        }).FirstOrDefault(parameters => parameters?.Count > 0);
        if (parameters?.Count != origin.TypeArguments.Count) return;
        foreach (var (parameter, argument) in parameters.Zip(origin.TypeArguments))
            if (shadowed?.Contains(parameter.Name) != true) bindings[parameter.Name] = argument;
    }

    private void InferDisplayBindings(TypeExpression pattern, TypeExpression concrete, ISet<string> genericNames, Dictionary<string, TypeExpression> bindings)
    {
        if (snapshot.Checker is not { } checker) return;
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
        void Infer(TypeExpression left, TypeExpression right) => InferDisplayBindings(left, right, genericNames, bindings);
    }

    private bool HasUnboundDisplayParameter(TypeExpression type, ISet<string> parameters) => type switch {
        NamedTypeExpression named => !snapshot.Checker!.IsResolvedTypeIdentity(named) &&
            (named.Namespace == null && named.TypeArguments.Count == 0 && parameters.Contains(named.Name) || named.TypeArguments.Any(argument => HasUnboundDisplayParameter(argument, parameters))),
        PointerTypeExpression pointer => HasUnboundDisplayParameter(pointer.Inner, parameters),
        ManagedTypeExpression managed => HasUnboundDisplayParameter(managed.Inner, parameters) || managed.Collector != null && HasUnboundDisplayParameter(managed.Collector, parameters),
        ReferenceTypeExpression reference => HasUnboundDisplayParameter(reference.Inner, parameters),
        ArrayTypeExpression array => HasUnboundDisplayParameter(array.ElementType, parameters),
        FunctionPointerTypeExpression function => HasUnboundDisplayParameter(function.ReturnType, parameters) || function.ParameterTypes.Any(parameter => HasUnboundDisplayParameter(parameter, parameters)),
        NestedTypeExpression nested => HasUnboundDisplayParameter(nested.Parent, parameters) || nested.TypeArguments.Any(argument => HasUnboundDisplayParameter(argument, parameters)),
        _ => false
    };
}
