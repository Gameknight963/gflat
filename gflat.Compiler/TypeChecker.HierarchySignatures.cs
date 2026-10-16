using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private TypeExpression ResolveHierarchyMethodType(MethodDeclaration method, TypeExpression type)
    {
        // A generic class can have its hierarchy checked synchronously while a
        // caller instantiates it. Its cloned signatures still use source aliases.
        using var declarations = IsolateDeclarationContext();
        using var nullable = IsolateNullableFlow();
        using var exceptions = IsolateExceptionHandlers();
        using var source = SourceContext.Enter(type.Span.Source != null ? type.Span : method.Span);
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        try
        {
            string ns = declarationNamespaces.GetValueOrDefault(method) ?? "";
            _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
            _currentNamespacePath = ns;
            _currentClass = null;
            _currentStruct = null;
            if (TryGetMethodOwner(method, out string owner, out bool isClass))
            {
                if (isClass) _currentClass = GetClass(owner);
                else _currentStruct = GetStruct(owner);
            }
            return ResolveAlias(type);
        }
        finally
        {
            _currentNamespace = previousNamespace;
            _currentNamespacePath = previousPath;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
        }
    }
}
