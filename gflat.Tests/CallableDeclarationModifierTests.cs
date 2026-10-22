using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class CallableDeclarationModifierTests
{
    public static IEnumerable<object[]> InvalidModifiers()
    {
        foreach(string owner in new[]{"struct","class"}) {
            foreach(string modifier in new[]{"readonly","virtual","override","abstract"})
                yield return [owner,modifier,"S(){}","Constructors cannot be readonly, virtual, override, or abstract"];
            foreach(string modifier in new[]{"static","readonly","virtual","override","abstract"})
                yield return [owner,modifier,"copy S(immutable(S)* other){}","Copy constructors cannot be static, readonly, virtual, override, or abstract"];
            foreach(string modifier in new[]{"static","const","readonly","override","abstract"})
                yield return [owner,modifier,"~S(){}","Destructors cannot be static, const, readonly, override, or abstract"];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidModifiers))]
    public void UnsupportedModifiersHaveSourceDiagnostics(string owner,string modifier,string declaration,string expected)
    {
        string text=$$$"""
            {{{owner}}} S {
                public {{{modifier}}} {{{declaration}}}
            }
            """;
        var source=new SourceFile("callable-modifier.gf",text);
        var diagnostic=Assert.Single(Compiler.Analyze([source],new(){NoStdLib=true}));
        Assert.Equal(expected,diagnostic.Message);
        Assert.Equal(modifier,text.Substring(diagnostic.Span.Start,diagnostic.Span.Length));
        Assert.Equal(source.Path,diagnostic.Span.Source?.Path);
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        Assert.Equal(expected,error.Description);
        Assert.Equal(diagnostic.Span,error.Span);
    }

    [Theory]
    [InlineData("public inline const immutable S(){}","immutable")]
    [InlineData("public inline const static copy S(immutable(S)* other){}","static")]
    [InlineData("public inline virtual override ~S(){}","override")]
    public void TheDiagnosticSkipsValidEarlierModifiers(string declaration,string expected)
    {
        string text="class S{"+declaration+"}";
        var error=Assert.Single(Compiler.Analyze([new SourceFile("ordered-modifiers.gf",text)],new(){NoStdLib=true}));
        Assert.Equal(expected,text.Substring(error.Span.Start,error.Span.Length));
    }

    [Fact]
    public void ConstInlineAndCopyConstructorsKeepTheirBehavior()
    {
        const string source="""
            struct Literal {public int value;public const Literal(int item){value=item;}}
            struct S {
                public int value;
                public inline const S(int item){value=item;}
                public inline copy S(immutable(S)* other){value=other.value+1;}
            }
            const Literal folded=new Literal(20);
            int main(){S first=new S(21);S second=first;return folded.value+second.value;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void VirtualAndInlineDestructorsKeepTheirBehavior()
    {
        const string source="""
            static class State {public static int count;}
            class Base {public inline virtual ~Base(){State::count+=2;}}
            class Derived:Base {public inline ~Derived(){State::count+=40;}}
            int main(){Base* value=(Base*)new* Derived();delete value;return State::count;}
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void StaticConstructorsRetainInlineAndNothrowSupport()
    {
        const string source="""
            static class State {
                public static int value;
                static inline State() nothrow {value=42;}
            }
            int main()=>State::value;
            """;
        Assert.Equal(42,CompilerTestHelper.Run(source).ExitCode);
    }
}
