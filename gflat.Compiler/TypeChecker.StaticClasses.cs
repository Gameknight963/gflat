using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private static void ValidateStaticClass(ClassDeclaration node)
    {
        if (!node.IsStatic) return;
        if (node.IsAbstract)
            throw new TypeCheckException("A static class cannot be abstract", node.Span);
        if (node.BaseClass != null || node.Interfaces.Count != 0)
            throw new TypeCheckException("Static classes cannot have base types or instance interfaces", node.Span);
        foreach (var member in node.Members)
        {
            string? error = member switch
            {
                ConstructorDeclaration => "Static classes cannot have instance constructors",
                DestructorDeclaration => "Static classes cannot have destructors",
                FieldDeclaration { IsStatic: false, IsConst: false } => "Fields in a static class must be static or const",
                MethodDeclaration { IsStatic: false } => "Methods in a static class must be static",
                MethodDeclaration { IsVirtual: true } or MethodDeclaration { IsOverride: true } or
                    MethodDeclaration { IsAbstract: true } => "Static class methods cannot be virtual, override, or abstract",
                MethodDeclaration { IsReadOnly: true } => "Static methods have no receiver to mark readonly",
                PropertyDeclaration p when p.Getter is { IsStatic: false } || p.Setter is { IsStatic: false }
                    => "Properties in a static class must be static",
                _ => null
            };
            if (error != null) throw new TypeCheckException(error, member.Span);
        }
    }
}
