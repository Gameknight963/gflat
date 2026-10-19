using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private static TypeExpression BindGenericConstraint(List<GenericParameter> parameters,
        List<TypeExpression> arguments, TypeExpression constraint)
        => new AstCloner(parameters.Zip(arguments).ToDictionary(pair => pair.First.Name, pair => pair.Second)).CloneType(constraint);

    private NamedTypeExpression MonomorphizeInterface(InterfaceDeclaration definition,
        List<TypeExpression> arguments, int line, Action<TypeExpression> publish)
    {
        ValidateAliasSpecialization(definition, definition.GenericParameters, arguments);
        if (arguments.Count != definition.GenericParameters.Count)
            throw new TypeCheckException($"Generic interface '{DisplayName(definition.Name)}' expects {definition.GenericParameters.Count} type arguments, but got {arguments.Count}", line);
        for (int i = 0; i < arguments.Count; i++)
        {
            var parameter = definition.GenericParameters[i];
            if (parameter.Constraint != null && !TypeSatisfiesDeclaredConstraint(definition, arguments[i],
                BindGenericConstraint(definition.GenericParameters, arguments, parameter.Constraint)))
                throw new TypeCheckException($"Type '{DisplayTypeName(arguments[i])}' does not satisfy constraint '{DisplayTypeName(parameter.Constraint)}' for type parameter '{parameter.Name}' on interface '{DisplayName(definition.Name)}'", line);
        }
        string name = definition.Name + "$" + string.Join("$", arguments.Select(GetTypeMangledName));
        extensionGenericTypeOrigins[name] = new NamedTypeExpression(definition.Name, null, line, arguments);
        _displayNames[name] = DisplayName(definition.Name) + "<" + string.Join(", ", arguments.Select(DisplayTypeName)) + ">";
        CheckFailedTypeSpecialization(name, line);
        var identity = NamedTypeExpression.Resolved(name, line);
        if (_interfaces.ContainsKey(name)) { publish(identity); return identity; }
        using var specialization = EnterTypeSpecialization(name, line);
        var substitutions = definition.GenericParameters.Select((p, i) => (p.Name, Type: arguments[i]))
            .ToDictionary(p => p.Name, p => p.Type);
        var specialized = new AstCloner(substitutions, definition.Name, name).CloneInterface(definition);
        _compilationUnit?.Members.Add(specialized);
        RegisterMemberInScope(specialized, _globalScope, "", namespaceVisible: false);
        declarationNamespaces[specialized] = declarationNamespaces.GetValueOrDefault(definition) ?? "";
        publish(identity);
        CheckSpecialization(definition, specialized);
        return identity;
    }
}
