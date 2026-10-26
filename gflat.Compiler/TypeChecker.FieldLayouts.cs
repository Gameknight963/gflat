using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private IDisposable EnterFieldLayoutContext(string owner, string member)
    {
        var previousNamespace = _currentNamespace; var previousPath = _currentNamespacePath;
        var previousClass = _currentClass; var previousStruct = _currentStruct;
        var declarationScope = IsolateDeclarationContext();
        var nullableScope = IsolateNullableFlow();
        var exceptionScope = IsolateExceptionHandlers();
        var cls = _classes.GetValueOrDefault(owner);
        var str = _structs.GetValueOrDefault(owner);
        var declaration = cls?.FieldDeclarationsByName.GetValueOrDefault(member) ?? str?.FieldDeclarationsByName.GetValueOrDefault(member);
        if (declaration != null && defaultConstantContexts.TryGetValue(declaration, out var context))
        {
            _currentNamespace = context.Namespace; _currentNamespacePath = context.Path;
            _currentClass = context.Class; _currentStruct = context.Struct;
        }
        else
        {
            string ns = cls?.Namespace ?? str?.Namespace ?? "";
            if (extensionGenericTypeOrigins.TryGetValue(owner, out var origin))
            {
                AstNode? definition = _genericStructs.GetValueOrDefault(origin.Name) ?? (AstNode?)_genericClasses.GetValueOrDefault(origin.Name);
                if (definition != null) ns = declarationNamespaces.GetValueOrDefault(definition) ?? ns;
            }
            if (declaration != null) ns = declarationNamespaces.GetValueOrDefault(declaration) ?? ns;
            _currentNamespacePath = ns.Replace("$", "::");
            _currentNamespace = _currentNamespacePath.Length == 0 ? _globalScope : ResolveNamespaceByName(_currentNamespacePath) ?? _globalScope;
            _currentClass = cls; _currentStruct = str;
        }
        var source = SourceContext.Enter(declaration?.Span ?? SourceContext.Current ?? default);
        return new LayoutScope(() => {
            source.Dispose();
            _currentNamespace = previousNamespace; _currentNamespacePath = previousPath;
            _currentClass = previousClass; _currentStruct = previousStruct;
            exceptionScope.Dispose(); nullableScope.Dispose(); declarationScope.Dispose();
        });
    }

    private TypeExpression ResolveFieldLayoutType(TypeExpression type)
    {
        // Keep source aliases for presentation, but bind their semantic identity
        // before the emitter or a caller queries the field from another scope.
        return resolvedSourceTypes[type] = ResolveAlias(type);
    }

    private int GetClassAlignment(ClassInfo info)
    {
        using var layout = EnterLayoutQuery(info.Name, SourceContext.Current?.Line ?? 0);
        int alignment = TargetInfo.Default.PointerBytes;
        foreach (var field in info.Fields)
        {
            using var context = EnterFieldLayoutContext(field.DeclaringClass, field.Name);
            alignment = Math.Max(alignment, GetTypeAlignment(ResolveFieldLayoutType(field.Type)));
        }
        return alignment;
    }

    private TypeExpression ResolveFieldLayoutType(string owner, string member, TypeExpression type)
    {
        using var context = EnterFieldLayoutContext(owner, member);
        return ResolveFieldLayoutType(type);
    }

}
