using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<ConstructorDeclaration> checkedCompileTimeConstructors = new();

    public void EnsureCompileTimeConstructorBody(ConstructorDeclaration constructor, string owner)
    {
        if (!checkedCompileTimeConstructors.Add(constructor)) return;
        using var context = EnterSignatureContext(constructor, owner, IsClassByIdentity(owner));
        using var bodyDemand = DemandEditorBody();
        var previous = _currentConstructor;
        try { constructor.Accept(this); }
        finally { _currentConstructor = previous; }
    }
}
