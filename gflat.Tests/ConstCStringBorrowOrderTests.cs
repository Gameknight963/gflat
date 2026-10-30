using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstCStringBorrowOrderTests
{
    [Fact]
    public void CStringScalarReceiverCapturesItsPointerAndIndexOnceBeforeArguments()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            struct State {
                public int calls;
                public const State() { calls = 0; }
                public const readonly(char)* Select() { calls++; return "*"; }
                public const int Index() { calls++; return 0; }
                public const int After() { calls++; return 0; }
            }
            extension char { public const readonly int Read(int extra) => (int)*this + extra; }
            const int Fold() {
                State state = new State();
                return state.Select()[state.Index()].Read(state.After()) + state.calls * 100;
            }
            const int answer = Fold();
            int main() => answer - 300;
            """).ExitCode);

    [Fact]
    public void CastingAwayReadonlyCannotMakeConstantStringStorageWritable()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            extension char { public const void Set() { *this = 'x'; } }
            const int Fold() { char* text = (char*)"*"; text[0].Set(); return 0; }
            const int answer = Fold();
            int main() => answer;
            """));
        Assert.Contains("immutable constant C-string storage", error.Message);
    }
}
