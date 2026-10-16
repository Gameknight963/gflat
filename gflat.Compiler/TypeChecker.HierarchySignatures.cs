using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private void BindMethodSignature(MethodDeclaration method)
    {
        if (method.IsGeneric) return;
        using var context = EnterMethodSignatureContext(method);
        method.ReturnType = ResolveAlias(method.ReturnType);
        foreach (var parameter in method.Parameters)
            parameter.Type = ResolveAlias(parameter.Type);
    }

    private TypeExpression ResolveHierarchyMethodType(MethodDeclaration method, TypeExpression type)
    {
        using var context = EnterMethodSignatureContext(method);
        using var source = SourceContext.Enter(type.Span.Source != null ? type.Span : method.Span);
        return ResolveAlias(type);
    }

    private IDisposable EnterMethodSignatureContext(MethodDeclaration method)
    {
        bool found = TryGetMethodOwner(method, out string owner, out bool isClass);
        return EnterSignatureContext(method, found ? owner : null, isClass);
    }

    private IDisposable EnterSignatureContext(AstNode declaration, string? owner, bool isClass)
    {
        // Generic declarations can be inspected while a caller instantiates
        // them, before body checking has bound their source signature aliases.
        var declarations = IsolateDeclarationContext();
        var nullable = IsolateNullableFlow();
        var exceptions = IsolateExceptionHandlers();
        var source = SourceContext.Enter(declaration.Span);
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        try
        {
            string ns = declarationNamespaces.GetValueOrDefault(declaration) ?? "";
            _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
            _currentNamespacePath = ns;
            _currentClass = owner != null && isClass ? _classes.GetValueOrDefault(owner) : null;
            _currentStruct = owner != null && !isClass ? _structs.GetValueOrDefault(owner) : null;
            return new NullableAction(Restore);
        }
        catch { Restore(); throw; }

        void Restore()
        {
            _currentNamespace = previousNamespace;
            _currentNamespacePath = previousPath;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
            source.Dispose();
            exceptions.Dispose();
            nullable.Dispose();
            declarations.Dispose();
        }
    }
}
