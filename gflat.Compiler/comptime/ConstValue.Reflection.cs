using gflat.ast;

namespace gflat.comptime;

public abstract partial record ConstValue
{
    // Identity refers to the compiler entity, independently of its display name.
    // Type retains the per-use alias/generic contract view for subsequent splicing.
    public sealed record Reflection(string Identity, string Kind, string Name, TypeExpression? Type,
        AstNode? Declaration = null, TypeExpression? DeclaringType = null, ConstValue? Value = null,
        IReadOnlyList<MethodDeclaration>? Overloads = null) : ConstValue;
}
