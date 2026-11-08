using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<MethodDeclaration, MethodDeclaration[]> functionDeclarations = new();

    public string GetNamespaceFunctionSymbol(MethodDeclaration method)
    {
        if (method.NativeSymbol != null) return method.NativeSymbol;
        string scope = GetFunctionNamespace(method);
        return "gflat$" + (scope.Length == 0 ? "" : scope + "$") + method.Name + GetOverloadSuffix(method);
    }

    private List<Parameter> VisibleFunctionParameters(MethodDeclaration method, SourceSpan use)
    {
        if (!functionDeclarations.TryGetValue(method, out var declarations)) return method.Parameters;
        var visible = declarations.Where(declaration =>
            declaration.Span.DeclarationSourceId == use.DeclarationSourceId ||
            declaration.Span.DeclarationSourceId is not { } owner ||
            _compilationUnit?.FileDeclarationScopes.GetValueOrDefault(owner) != DeclarationScope.File).ToArray();
        foreach (var declaration in visible)
        {
            using var context = EnterMethodSignatureContext(method);
            ValidateParameterDefaults(declaration, declaration.Parameters);
        }
        for (int index = 0; index < method.Parameters.Count; index++)
        {
            var defaults = visible.Select(declaration => declaration.Parameters[index])
                .Where(parameter => parameter.DefaultValue != null).ToArray();
            if (defaults.Length > 1 && defaults.Skip(1).Any(parameter => ParameterDefault(parameter) != ParameterDefault(defaults[0])))
                throw new TypeCheckException($"Visible declarations of '{method.Name}' specify conflicting defaults", use);
        }
        return visible.OrderByDescending(declaration => declaration.Parameters.Count(parameter => parameter.DefaultValue != null))
            .FirstOrDefault()?.Parameters ?? method.Parameters;
    }

    private void ResolveFunctionDeclarations(List<FunctionEntry> functions)
    {
        if (!functions.Any(entry => entry.Method.IsPrototype)) return;
        foreach (var group in functions.GroupBy(entry => ParameterKey(entry.Method)).ToArray())
        {
            var declarations = group.ToArray();
            var prototypes = declarations.Where(entry => entry.Method.IsPrototype).ToArray();
            if (prototypes.Length == 0) continue;
            if (declarations.Any(entry => entry.Method.ImplementationKind != FunctionImplementationKind.Ordinary))
                throw new TypeCheckException("Ordinary function declarations cannot be combined with expect, weak or replace", prototypes[0].Method.Span);
            var definitions = declarations.Where(entry => !entry.Method.IsPrototype).ToArray();
            if (definitions.Length > 1)
                DuplicateDeclaration(definitions[0].Method, definitions[1].Method, $"Duplicate definition of '{definitions[0].Method.Name}'");
            var selected = definitions.FirstOrDefault() ?? prototypes[0];
            var nativeSymbols = declarations.Select(entry => entry.Method.NativeSymbol).OfType<string>().Distinct().ToArray();
            if (nativeSymbols.Length > 1)
                throw new TypeCheckException($"Declarations of '{selected.Method.Name}' refer to different library symbols", selected.Method.Span);
            if (nativeSymbols.Length == 1) selected.Method.NativeSymbol = nativeSymbols[0];
            foreach (var entry in declarations)
                if (!ContractMatches(selected.Method, entry.Method))
                    throw new TypeCheckException($"Declaration of '{entry.Method.Name}' must match the function's return type, constraints, accessibility and modifiers", entry.Method.Span);
            functionDeclarations[selected.Method] = declarations.Select(entry => entry.Method).ToArray();
            foreach (var entry in prototypes)
            {
                if (ReferenceEquals(entry, selected)) continue;
                entry.Members.Remove(entry.Method);
                functions.Remove(entry);
            }
        }
    }

    public bool IsDeclarationDiscoverable(AstNode declaration, SourceSpan use)
    {
        bool Visible(AstNode node) => node.Span.DeclarationSourceId is not { } owner ||
            owner == use.DeclarationSourceId ||
            _compilationUnit?.FileDeclarationScopes.GetValueOrDefault(owner) != DeclarationScope.File;
        return Visible(declaration) || declaration is MethodDeclaration method &&
            functionDeclarations.TryGetValue(method, out var declarations) && declarations.Any(Visible);
    }
}
