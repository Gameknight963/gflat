using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<AstNode> _copyUses = new();
    public bool IsCopyUse(AstNode expression) => _copyUses.Contains(expression);

    public ConstructorDeclaration? CopyConstructor(TypeExpression type)
    {
        type = ResolveAlias(type);
        if (type is not NamedTypeExpression n) return null;
        return (GetStruct(n.Name)?.Constructors ?? GetClass(n.Name)?.Constructors)?.FirstOrDefault(c => c.IsCopy);
    }

    public bool RequiresCopy(TypeExpression type)
    {
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression a) return RequiresCopy(a.ElementType);
        if (CopyConstructor(type) != null || HasDestructor(type)) return true;
        if (type is not NamedTypeExpression n) return false;
        var cls = GetClass(n.Name);
        if (cls?.BaseClass is string parent && RequiresCopy(new NamedTypeExpression(parent, null, type.Line))) return true;
        var fields = GetStruct(n.Name)?.Fields.Select(f => f.Type) ?? cls?.Fields.Select(f => f.Type);
        return fields?.Any(RequiresCopy) == true;
    }

    public bool CanCopy(TypeExpression type)
    {
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression a) return a.Size != null && CanCopy(a.ElementType);
        if (CopyConstructor(type) != null) return true;
        if (type is not NamedTypeExpression n) return true;
        var str = GetStruct(n.Name);
        var cls = GetClass(n.Name);
        // A user-written destructor describes a resource contract we cannot infer.
        if (str?.Destructor != null || cls?.Destructor != null) return false;
        if (cls?.BaseClass is string parent && !CanCopy(new NamedTypeExpression(parent, null, type.Line))) return false;
        return (str?.Fields.Select(f => f.Type) ?? cls?.Fields.Select(f => f.Type))?.All(CanCopy) ?? true;
    }

    private bool CanAccessCopy(TypeExpression type, string? accessor)
    {
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression array) return CanAccessCopy(array.ElementType, accessor);
        if (type is not NamedTypeExpression named) return true;
        if (CopyConstructor(type) is ConstructorDeclaration copy)
            return copy.Accessibility switch {
                TokenKind.Private => CanAccessPrivate(accessor, named.Name),
                TokenKind.Protected => accessor != null && (accessor == named.Name || IsSubclassOf(accessor, named.Name)),
                _ => true
            };
        var cls = GetClass(named.Name);
        if (cls?.BaseClass is string parent && !CanAccessCopy(new NamedTypeExpression(parent, null, type.Line), named.Name)) return false;
        var fields = GetStruct(named.Name)?.Fields.Select(f => f.Type) ?? cls?.Fields.Select(f => f.Type);
        return fields?.All(f => CanAccessCopy(f, named.Name)) ?? true;
    }

    private bool CopyCanThrow(TypeExpression type)
    {
        type = ResolveAlias(type);
        if (type is ArrayTypeExpression array) return CopyCanThrow(array.ElementType);
        if (CopyConstructor(type) is ConstructorDeclaration copy) return !copy.IsNoThrow;
        if (type is not NamedTypeExpression named) return false;
        var cls = GetClass(named.Name);
        if (cls?.BaseClass is string parent && CopyCanThrow(new NamedTypeExpression(parent, null, type.Line))) return true;
        return (GetStruct(named.Name)?.Fields.Select(f => f.Type) ?? cls?.Fields.Select(f => f.Type))?.Any(CopyCanThrow) == true;
    }

    public bool CopiesValue(AstNode expression, TypeExpression target)
        => !IsFreshValue(expression) && RequiresCopy(target);

    private bool ValidateCopyUse(AstNode expression, TypeExpression target)
    {
        if (!CopiesValue(expression, target)) return true;
        if (!CanCopy(target)) return false;
        if (!CanAccessCopy(target, _currentClass?.Name ?? _currentStruct?.Name)) return false;
        if (_currentFunction is MethodDeclaration { IsConst: true } || _currentConstructor?.IsConst == true)
            throw new TypeCheckException("Nontrivial copying is not supported during constant evaluation", expression.Span);
        if (CopyCanThrow(target))
        {
            _copyUses.Add(expression);
            RecordImplicitException();
        }
        return true;
    }

    private void RecordImplicitException()
    {
        if (_currentFunction is not MethodDeclaration caller || caller.IsNoThrow) return;
        if (_tryStack.SelectMany(t => t.CatchClauses).Any(c => c.ExceptionType == null ||
            ResolveAlias(c.ExceptionType) is PointerTypeExpression { Inner: NamedTypeExpression { Name: "Exception" } } or ReferenceTypeExpression { Inner: NamedTypeExpression { Name: "Exception" } })) return;
        if (!_directExceptions.TryGetValue(caller, out var exceptions)) _directExceptions[caller] = exceptions = new();
        exceptions.Add(UnknownExceptionType);
        _exceptionSummaries = null;
    }
}
