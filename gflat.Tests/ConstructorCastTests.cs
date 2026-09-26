namespace gflat.Tests;

public class ConstructorCastTests
{
    [Fact]
    public void ConstructorArgumentWithReadonlyPointerCastIsNotAFunctionPointerType()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct View {
                readonly(char)* text;
                public View(readonly(char)* p) { text = p; }
                public readonly char First() { return text[0]; }
            }
            int main() {
                char[2] data = "x";
                View view = new View((readonly(char)*)data);
                if (view.First() != 'x') return 1; return 0;
            }
            """).ExitCode);
}
