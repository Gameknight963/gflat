using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record FunctionEntry(MethodDeclaration Method, List<AstNode> Members,
        NamespaceScope Scope, AstNode? Owner);

    private void ResolveFunctionReplacements(CompilationUnit unit)
    {
        var entries = new List<FunctionEntry>();
        Collect(unit.Members, _globalScope, null);
        foreach (NamespaceDeclaration ns in unit.Namespaces) Collect(ns.Members, _namespaceScopes[ns], null);
        var previousNamespace = _currentNamespace;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        try
        {
            foreach (var group in entries.GroupBy(e => (e.Scope, e.Owner, e.Method.Name)))
            {
                _currentNamespace = group.Key.Scope;
                _currentClass = group.Key.Owner is ClassDeclaration cls ? GetClass(cls.Name) : null;
                _currentStruct = group.Key.Owner is StructDeclaration str ? GetStruct(str.Name) : null;
                var functions = group.ToList();
                foreach (FunctionEntry entry in functions)
                {
                    MethodDeclaration method = entry.Method;
                    if (method.IsStatic && (method.IsVirtual || method.IsOverride || method.IsAbstract))
                        throw new TypeCheckException("Static methods cannot be virtual, override or abstract", method.Line);
                    if (method.ImplementationKind != FunctionImplementationKind.Ordinary &&
                        (method.Body == null || method.IsAbstract || entry.Owner is InterfaceDeclaration))
                        throw new TypeCheckException("weak and replace require a concrete function body", method.Line);
                    if (method.ImplementationKind != FunctionImplementationKind.Replace) continue;
                    var defaults = functions.Where(e => e.Method.ImplementationKind == FunctionImplementationKind.Weak &&
                        ParametersMatch(e.Method, method)).ToList();
                    if (defaults.Count != 1)
                        throw new TypeCheckException($"replace '{method.Name}' requires exactly one matching weak definition in the same scope", method.Line);
                    MethodDeclaration original = defaults[0].Method;
                    if (!ContractMatches(original, method))
                        throw new TypeCheckException($"Replacement of '{method.Name}' must match the weak function's return type, constraints, accessibility and modifiers", method.Line);
                }
                if (functions.Count == 1 && functions[0].Method.ImplementationKind == FunctionImplementationKind.Ordinary) continue;
                if (group.Key.Owner == null && group.Key.Scope.Externs.ContainsKey(group.Key.Name))
                    throw new TypeCheckException($"Function '{group.Key.Name}' cannot have both an extern declaration and a weak or replace definition", functions[0].Method.Line);
                if (functions.Any(e => e.Method.ImplementationKind != FunctionImplementationKind.Ordinary) &&
                    functions.Select(e => ParameterKey(e.Method)).Distinct().Count() > 1)
                    throw new TypeCheckException($"Overloading weak function '{group.Key.Name}' is not supported; replacements must match its signature", functions[1].Method.Line);
                foreach (var sameSignature in functions.GroupBy(e => ParameterKey(e.Method)))
                {
                    var declarations = sameSignature.ToList();
                    if (declarations.Count == 1) continue;
                    var weak = declarations.Where(e => e.Method.ImplementationKind == FunctionImplementationKind.Weak).ToList();
                    var replace = declarations.Where(e => e.Method.ImplementationKind == FunctionImplementationKind.Replace).ToList();
                    if (weak.Count > 1)
                        throw new TypeCheckException($"Multiple weak defaults for '{group.Key.Name}'", weak[1].Method.Line);
                    if (replace.Count > 1)
                        throw new TypeCheckException($"Multiple replacements for '{group.Key.Name}'", replace[1].Method.Line);
                    if (weak.Count != 1 || replace.Count != 1 || declarations.Count != 2)
                        throw new TypeCheckException($"Duplicate definition of '{group.Key.Name}'; replacing a weak function requires replace", declarations[1].Method.Line);
                    // Remove the unused body before checking bodies or generating code.
                    weak[0].Members.Remove(weak[0].Method);
                }
            }
        }
        finally
        {
            _currentNamespace = previousNamespace;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
        }

        void Collect(List<AstNode> members, NamespaceScope scope, AstNode? owner)
        {
            foreach (AstNode node in members)
            {
                if (node is MethodDeclaration method)
                {
                    if (method.StringLiteralPrefix == null) entries.Add(new(method, members, scope, owner));
                }
                else if (node is NamespaceDeclaration ns) Collect(ns.Members, _namespaceScopes[ns], null);
                else if (node is ClassDeclaration cls) Collect(cls.Members, scope, cls);
                else if (node is StructDeclaration str) Collect(str.Members, scope, str);
                else if (node is InterfaceDeclaration iface) Collect(iface.Members, scope, iface);
            }
        }
    }

    private string SignatureType(TypeExpression type, MethodDeclaration method)
    {
        if (type is NamedTypeExpression generic && generic.Namespace == null)
        {
            int index = method.GenericParameters.FindIndex(p => p.Name == generic.Name);
            if (index >= 0) return "$type" + index;
        }
        // Preserve generic shapes without instantiating them during symbol selection.
        if (type is NamedTypeExpression { TypeArguments.Count: > 0 } applied)
            return applied.Namespace + "::" + applied.Name + "<" + string.Join(",", applied.TypeArguments.Select(t => SignatureType(t, method))) + ">";
        // Resolve aliases without instantiating generic pointees during this pre-body pass.
        if (type is NamedTypeExpression or NestedTypeExpression)
        {
            TypeExpression resolved = _symbols.ResolveAliasDirect(type);
            if (!ReferenceEquals(type, resolved)) return SignatureType(resolved, method);
        }
        return type switch
        {
            NamedTypeExpression named => (named.Namespace == null ? "" : named.Namespace + "::") + named.Name,
            PointerTypeExpression p => $"{(p.IsReadOnly ? "readonly " : "")}{SignatureType(p.Inner, method)}*{(p.IsNullable ? "?" : "")}",
            ManagedTypeExpression p => $"{(p.IsReadOnly ? "readonly " : "")}{SignatureType(p.Inner, method)}^{(p.IsNullable ? "?" : "")}",
            ArrayTypeExpression a => SignatureType(a.ElementType, method) + $"[{((ArrayTypeExpression)ResolveAlias(new ArrayTypeExpression(Int, a.Size, a.Line, a.SizeExpression))).Size}]",
            FunctionPointerTypeExpression f => SignatureType(f.ReturnType, method) + "(" + string.Join(",", f.ParameterTypes.Select(t => SignatureType(t, method))) + $"){(f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => TypeName(type)
        };
    }

    private string ParameterKey(MethodDeclaration method) => method.GenericParameters.Count + ":" +
        string.Join(",", method.Parameters.Select(p => SignatureType(p.Type, method)));

    private bool ParametersMatch(MethodDeclaration first, MethodDeclaration second) => ParameterKey(first) == ParameterKey(second);

    private bool ContractMatches(MethodDeclaration first, MethodDeclaration second) =>
        SignatureType(first.ReturnType, first) == SignatureType(second.ReturnType, second) &&
        first.Accessibility == second.Accessibility && first.IsStatic == second.IsStatic &&
        first.IsVirtual == second.IsVirtual && first.IsOverride == second.IsOverride &&
        first.IsConst == second.IsConst && first.IsReadOnly == second.IsReadOnly && first.Throws == second.Throws &&
        first.Parameters.Select(p => p.IsConst).SequenceEqual(second.Parameters.Select(p => p.IsConst)) &&
        first.GenericParameters.Select(p => p.Constraint == null ? "" : SignatureType(p.Constraint, first))
            .SequenceEqual(second.GenericParameters.Select(p => p.Constraint == null ? "" : SignatureType(p.Constraint, second)));
}
