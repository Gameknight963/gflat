using gflat.ast;

namespace gflat.Tests;

public sealed class GenericQualifierCloningTests
{
    public static TheoryData<string, bool> Owners => new()
    {
        { "class", false }, { "class", true }, { "struct", false }, { "struct", true }
    };

    [Theory]
    [MemberData(nameof(Owners))]
    public void QualifiedSelfReadUsesTheSameStaticStorage(string kind, bool namespaced)
    {
        string source = Wrap(kind, namespaced,
            "public static int seed=42;public int Check(){seed=40;return Entry<T>::seed+2;}",
            prefix => "int main(){" + prefix + "Entry<int> value=new " + prefix + "Entry<int>();return value.Check();}");
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class", false, false)]
    [InlineData("class", false, true)]
    [InlineData("class", true, false)]
    [InlineData("class", true, true)]
    [InlineData("struct", false, false)]
    [InlineData("struct", false, true)]
    [InlineData("struct", true, false)]
    [InlineData("struct", true, true)]
    public void QualifiedSelfWriteKeepsSpecializationsIndependent(string kind, bool namespaced, bool reverse)
    {
        string first = reverse ? "long" : "int";
        string second = reverse ? "int" : "long";
        string source = Wrap(kind, namespaced,
            "public static int seed=42;public static int Check(int next){Entry<T>::seed=next;return seed;}",
            prefix => "int main(){int first=" + prefix + "Entry<" + first + ">::Check(11);int second=" + prefix + "Entry<" + second + ">::Check(22);return first+second;}");
        Assert.Equal(33, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class", false, "private")]
    [InlineData("class", true, "private")]
    [InlineData("struct", false, "private")]
    [InlineData("struct", true, "private")]
    [InlineData("class", false, "protected")]
    [InlineData("class", true, "protected")]
    [InlineData("struct", false, "protected")]
    [InlineData("struct", true, "protected")]
    public void QualifiedSelfCanAccessItsOwnRestrictedStorage(string kind, bool namespaced, string visibility)
    {
        string source = Wrap(kind, namespaced,
            visibility + " static int seed=40;public static int Read()=>Entry<T>::seed+2;",
            prefix => "int main()=>" + prefix + "Entry<int>::Read();");
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    public void TypeQualifierArgumentsAreClonedAsConcreteNodes(string primitive)
    {
        var file = new SourceFile("qualifier.gf", "Other<T>::seed");
        var argument = new NamedTypeExpression("T", null, 1) { Span = new(6, 1, 1, 7, file) };
        var qualifier = new NamedTypeExpression("Other", null, 1, [argument])
        {
            Span = new(0, 8, 1, 1, file), NameSpan = new(0, 5, 1, 1, file)
        };
        var source = new NamespaceAccessExpression(qualifier, "seed", 1) { Span = new(0, 14, 1, 1, file) };
        var concrete = new NamedTypeExpression(primitive, null, 1);
        var cloned = Assert.IsType<NamespaceAccessExpression>(new AstCloner(new() { ["T"] = concrete }).CloneNode(source));
        var boundQualifier = Assert.IsType<NamedTypeExpression>(cloned.Left);
        Assert.NotSame(qualifier, boundQualifier);
        Assert.Same(concrete, Assert.Single(boundQualifier.TypeArguments));
        Assert.Equal(primitive, Assert.IsType<NamedTypeExpression>(boundQualifier.TypeArguments[0]).Name);
        Assert.Equal("T", Assert.IsType<NamedTypeExpression>(Assert.Single(qualifier.TypeArguments)).Name);
        Assert.Equal(qualifier.NameSpan, boundQualifier.NameSpan);
        Assert.Equal(qualifier.Span, boundQualifier.Span);
        Assert.Equal(source.Span, cloned.Span);
    }

    [Fact]
    public void ShadowingMethodParameterControlsAnAppliedQualifier()
        => Assert.Equal(1, CompilerTestHelper.Run("struct State<T>{public static int width=sizeof(T);}struct Entry<T>{public static int Read<T>()=>State<T>::width;}int main()=>Entry<int>::Read<byte>();").ExitCode);

    [Fact]
    public void NestedQualifierKeepsItsContainingSpecialization()
        => Assert.Equal(4, CompilerTestHelper.Run("struct Entry<T>{public struct Nested{public static int width=sizeof(T);}public static int Read()=>Entry<T>::Nested::width;}int main()=>Entry<int>::Read();").ExitCode);

    private static string Wrap(string kind, bool namespaced, string members, Func<string, string> consumer)
    {
        string declaration = kind + " Entry<T>{" + members + "}";
        return (namespaced ? "namespace Lib{" + declaration + "}" : declaration) + consumer(namespaced ? "Lib::" : "");
    }
}

