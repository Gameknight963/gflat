using gflat.diagnostics;

namespace gflat.Tests;

public sealed class NominalIdentityTests
{
    [Theory]
    [InlineData("Word")]
    [InlineData("Selected")]
    [InlineData("immutable(Word)")]
    [InlineData("immutable(Selected)")]
    public void ResolvedGlobalArgumentsCannotBeCapturedByNestedNames(string argument)
    {
        string source = $$"""
            struct Word { public int value; }
            alias Selected=Word;
            class Owner {
                public struct Word { public long value; }
                public struct Box<T> { public T value; public static int Size()=>sizeof(T); }
            }
            int main() {
                if(sizeof(Owner::Box<{{argument}}>) != 4) return 1;
                return Owner::Box<{{argument}}>::Size();
            }
            """;
        AssertValid(source);
        Assert.Equal(4, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void BoundNominalNameIsNotSubstitutedAsAnotherTemplateParameter()
    {
        const string source = """
            struct T { public int value; }
            struct Outer<X> {
                public struct Inner<T> { public X first; public T second; }
            }
            int main() { return sizeof(Outer<T>::Inner<byte>); }
            """;
        AssertValid(source);
        Assert.Equal(8, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void NominalPointerArgumentsKeepTheOriginalPointee()
    {
        const string source = """
            struct Word { public int value; }
            class Owner {
                public struct Word { public long value; }
                public struct Box<T> { public T value; public Box(T data){value=data;} }
            }
            int main() {
                Word word=new Word(); word.value=42;
                Owner::Box<Word*> box=new Owner::Box<Word*>(&word);
                return box.value.value;
            }
            """;
        AssertValid(source);
        Assert.Equal(42, CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void CleanupUsesTheOriginalGlobalArgumentDestructor()
    {
        const string source = """
            static class State { public static int count; }
            struct Word { ~Word(){State::count++;} }
            class Owner {
                public struct Word { ~Word(){State::count+=10;} }
                public struct Box<T> { public T value; }
            }
            int main() {
                { Owner::Box<Word> item=new Owner::Box<Word>(); }
                return State::count;
            }
            """;
        AssertValid(source);
        Assert.Equal(1, CompilerTestHelper.Run(source).ExitCode);
    }

    [Theory]
    [InlineData("Selected")]
    [InlineData("immutable(Selected)")]
    public void NominalContractViewsRemainRestrictedAfterGenericSubstitution(string selected)
    {
        string source = $$"""
            interface IProvider { static int Read(); }
            static class Answer:IProvider {
                public static int Read()=>42;
                public static int Extra()=>1;
            }
            weak alias Selected:IProvider=Answer;
            int Invoke<P>()=>P::Extra();
            int main()=>Invoke<{{selected}}>();
            """;
        Assert.Contains(Analyze(source), d=>d.Severity==DiagnosticSeverity.Error && d.Message.Contains("not exposed",StringComparison.Ordinal));
    }

    [Fact]
    public void BoundOuterNominalDoesNotBecomeAnInnerMethodTypeParameter()
    {
        const string source = """
            struct T { public int value; }
            struct Outer<X> {
                public static int Read<T>(X first,T second)=>sizeof(T);
            }
            int main() { T value=new T(); return Outer<T>::Read(value,(byte)1); }
            """;
        AssertValid(source);
        Assert.Equal(1,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void WeakReplacementSignaturesDistinguishBoundNominalsFromMethodParameters()
    {
        const string source = """
            struct T { public int value; }
            alias Selected=T;
            weak int Read<T>(Selected value)=>1;
            replace int Read<U>(Selected value)=>2;
            int main() { T value=new T(); return Read<int>(value); }
            """;
        AssertValid(source);
        Assert.Equal(2,CompilerTestHelper.Run(source).ExitCode);
    }

    private static IReadOnlyList<Diagnostic> Analyze(string source)=>
        Compiler.Analyze([new SourceFile("nominal-identity.gf",source)],new(){NoStdLib=true});

    private static void AssertValid(string source)=>
        Assert.DoesNotContain(Analyze(source), d=>d.Severity==DiagnosticSeverity.Error);
}
