using gflat.ast;

namespace gflat.Tests;

public sealed class RecursiveTypeProtectionDisplayTests
{
    private static NamedTypeExpression Named(string name) => new(name, null, 0);
    private static TypeExpression Read(TypeExpression type) => TypeQualifiers.ReadOnly(type);
    private static TypeExpression Deep(TypeExpression type) => TypeQualifiers.Immutable(type);
    private static ArrayTypeExpression ArrayOf(TypeExpression type, int count = 2) => new(type, count, 0);
    private static PointerTypeExpression Pointer(TypeExpression type, bool nullable = false) => new(type, nullable, 0);
    private static ManagedTypeExpression Managed(TypeExpression type, bool nullable = false, TypeExpression? collector = null)
        => new(type, nullable, 0, collector: collector);

    private static void AssertNames(TypeExpression type, string expected)
    {
        Assert.Equal(expected, new TypeChecker().DisplayTypeName(type));
        Assert.Equal(expected, TypeChecker.TypeName(type));
        Assert.Equal(expected, TypeChecker.TypeNamePublic(type));
    }

    [Fact]
    public void ShallowArrayProtectionStopsAtRawAndManagedIndirection()
    {
        AssertNames(Read(ArrayOf(Pointer(Read(Named("int"))))), "readonly(readonly(int)*[2])");
        AssertNames(Read(ArrayOf(Pointer(Read(Named("int")), nullable: true))), "readonly(readonly(int)*?[2])");
        AssertNames(Read(ArrayOf(Managed(Read(Named("int"))))), "readonly(readonly(int)^[2])");
        AssertNames(Read(ArrayOf(Managed(Read(Named("int")), nullable: true))), "readonly(readonly(int)^?[2])");
        AssertNames(Read(ArrayOf(Pointer(Pointer(Read(Named("int")))))), "readonly(readonly(int)**[2])");
        AssertNames(Read(ArrayOf(ArrayOf(Pointer(Read(Named("int"))), 3))), "readonly(readonly(int)*[3][2])");
        AssertNames(Read(ArrayOf(Pointer(Named("int")))), "readonly(int*[2])");
    }

    [Fact]
    public void ShallowArrayProtectionCannotHideStrongerImmutableChildren()
    {
        AssertNames(Read(ArrayOf(Deep(Named("Box")))), "readonly(immutable(Box)[2])");
        AssertNames(Read(ArrayOf(Pointer(Deep(Named("Box"))))), "readonly(immutable(Box)*[2])");
        AssertNames(Read(ArrayOf(Deep(Pointer(Named("Box"))))), "readonly(immutable(Box*)[2])");
        AssertNames(Read(ArrayOf(Deep(ArrayOf(Named("Box"), 3)))), "readonly(immutable(Box[3])[2])");
    }

    [Fact]
    public void DeepProtectionRemainsCompactAndCollectorNamesKeepFreshContext()
    {
        AssertNames(Deep(Pointer(Named("int"))), "immutable(int*)");
        AssertNames(Deep(ArrayOf(Pointer(Named("Box")))), "immutable(Box*[2])");
        AssertNames(Deep(ArrayOf(Managed(Named("Box"), nullable: true))), "immutable(Box^?[2])");
        AssertNames(Read(ArrayOf(Managed(Read(Named("int")), collector: Named("Collector")))),
            "readonly(managed<readonly(int), Collector>[2])");
        AssertNames(Deep(Managed(Named("Box"), collector: Read(Named("Collector")))),
            "immutable(managed<Box, readonly(Collector)>)");
        AssertNames(Read(Pointer(Read(Named("int")))), "readonly(readonly(int)*)");
    }

    [Fact]
    public void ReferencesAndCallableSignaturesRetainTheirOwnProtectionContext()
    {
        AssertNames(new ReferenceTypeExpression(Read(Named("int")), 0), "ref readonly(int)");
        var function = new FunctionPointerTypeExpression(Read(Named("int")), [Pointer(Deep(Named("Box")))], false, false, 0);
        AssertNames(Read(ArrayOf(function)), "readonly(readonly(int)(immutable(Box)*)*[2])");
        var borrowing = new FunctionPointerTypeExpression(Read(Named("int")),
            [new ReferenceTypeExpression(Deep(Named("Box")), 0)], false, false, 0);
        AssertNames(Read(ArrayOf(borrowing)), "readonly(readonly(int)(ref immutable(Box))*[2])");
    }

    [Fact]
    public void ExplicitGenericArgumentProtectionKeepsFreshContextAndDoesNotMutateAst()
    {
        TypeExpression argument = Pointer(Read(Named("int")));
        var named = new NamedTypeExpression("Box", null, 0, [argument]);
        TypeExpression type = Deep(named);
        Assert.Equal("immutable(Box<readonly(int)*>)", new TypeChecker().DisplayTypeName(type));
        // Static TypeName's existing unresolved-named-argument behavior is unchanged.
        Assert.Equal("immutable(Box)", TypeChecker.TypeName(type));
        Assert.True(type.IsImmutableValue);
        Assert.True(type.IsReadOnlyValue);
        Assert.False(argument.IsReadOnlyValue);
        Assert.True(((PointerTypeExpression)argument).Inner.IsReadOnlyValue);
        Assert.False(((PointerTypeExpression)argument).Inner.IsImmutableValue);
        Assert.Same(argument, named.TypeArguments[0]);
    }
}
