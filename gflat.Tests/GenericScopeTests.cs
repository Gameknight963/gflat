using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class GenericScopeTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void MethodParameterShadowsContainingTypeParameter(string kind)
    {
        string source = $$"""
            {{kind}} Box<T> {
                public static T Read<T>(T value)=>value;
                public static int Width<T>()=>sizeof(T);
            }
            int main() {
                if (Box<int>::Read<long>(4294967297l) != 4294967297l) return 99;
                return Box<int>::Width<byte>();
            }
            """;
        AssertValid(source);
        Assert.Equal(1, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void NestedTypeParameterShadowsOuterWhileOtherOuterParametersSubstitute(string kind)
    {
        string source = $$"""
            struct Outer<T,U> {
                public {{kind}} Inner<T> {
                    public U first;
                    public T second;
                    public static int Width()=>sizeof(U)+sizeof(T);
                }
            }
            int main()=>Outer<int,long>::Inner<byte>::Width();
            """;
        AssertValid(source);
        Assert.Equal(9, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ExplicitDifferentOwnerSpecializationIsNotRenamedToClosedOwner()
    {
        const string source = """
            struct Box<T> {
                public T value;
                public static int Other()=>sizeof(Box<long>);
            }
            int main()=>Box<int>::Other();
            """;
        AssertValid(source);
        Assert.Equal(8, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ExplicitOwnerArgumentCanReferToShadowingMethodParameter()
    {
        const string source = """
            struct Box<T> {
                public T value;
                public static int Width<T>()=>sizeof(Box<T>);
            }
            int main()=>Box<int>::Width<byte>();
            """;
        AssertValid(source);
        Assert.Equal(1, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ShadowedProviderParametersControlStaticCallsAndNameof()
    {
        const string source = """
            interface IProvider { static int Read(); }
            static class First:IProvider {public static int Read()=>10;}
            static class Second:IProvider {public static int Read()=>20;}
            struct Box<T:IProvider> {
                public static int Read<T:IProvider>()=>T::Read();
                public static int Initial<T>()=>(int)nameof(T)[0];
            }
            int main() {
                if(Box<First>::Read<Second>() != 20) return 99;
                return Box<First>::Initial<byte>();
            }
            """;
        AssertValid(source);
        Assert.Equal((int)'T', CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void ShadowingDoesNotChangeTheArityOfExplicitOwnerTypes()
    {
        const string source = """
            struct Box<T,U> {
                public static int Width<T>()=>sizeof(Box<U>);
            }
            int main()=>Box<int,long>::Width<byte>();
            """;
        Assert.Contains(Compiler.Analyze([new SourceFile("arity.gf",source)],new(){NoStdLib=true}),
            d=>d.Severity==DiagnosticSeverity.Error && d.Message.Contains("expects 2 type arguments"));
    }

    [Fact]
    public void RetainedGenericConstraintsAreClonedWithinTheirOwnScope()
    {
        var own = new NamedTypeExpression("T", null, 1);
        var outer = new NamedTypeExpression("U", null, 1);
        var method = new MethodDeclaration("Read", own, new(), null, TokenKind.Public,
            true, false, false, false, 1, genericParameters:
            [new GenericParameter("T", own, 1), new GenericParameter("V", outer, 1)]);
        var cloner = new AstCloner(new() {
            ["T"]=new NamedTypeExpression("int",null,1),
            ["U"]=new NamedTypeExpression("long",null,1)
        }, "Box", "Box$int$long");
        var clone=cloner.CloneMethod(method);
        Assert.Equal("T", Assert.IsType<NamedTypeExpression>(clone.ReturnType).Name);
        Assert.Equal("T", Assert.IsType<NamedTypeExpression>(clone.GenericParameters[0].Constraint).Name);
        Assert.Equal("long", Assert.IsType<NamedTypeExpression>(clone.GenericParameters[1].Constraint).Name);
        Assert.NotSame(method.GenericParameters[0], clone.GenericParameters[0]);
        Assert.Equal("U", Assert.IsType<NamedTypeExpression>(method.GenericParameters[1].Constraint).Name);
    }

    private static void AssertValid(string source) => Assert.DoesNotContain(Compiler.Analyze(
        [new SourceFile("generic-scope.gf",source)],new(){NoStdLib=true}),d=>d.Severity==DiagnosticSeverity.Error);
}


