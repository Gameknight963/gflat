using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AssignmentSnapshotTests
{
    [Theory]
    [InlineData("if (choose) value = 1;")]
    [InlineData("if (choose) value = 1; else { int value = 2; }")]
    [InlineData("while (choose) { value = 1; break; }")]
    public void AssignmentInOneBranchDoesNotInitializeOtherSnapshots(string branch)
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("int Read(bool choose) { int value; " + branch + " return value; }"));
        Assert.Contains("unassigned local variable 'value'", error.Message);
    }

    [Fact]
    public void FieldWritesRemainSeparateAcrossBranches()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            struct Pair { public int X; public int Y; }
            int Read(bool choose) {
                Pair pair;
                if (choose) pair.X = 1; else pair.Y = 2;
                return pair.X;
            }
            """));
        Assert.Contains("unassigned field 'X'", error.Message);
    }

    [Fact]
    public void BothBranchesCanInitializeAnOuterBindingDespiteShadowedLocals()
        => Check("""
            int Read(bool choose) {
                int value;
                if (choose) { value = 1; { int value = 2; } }
                else { value = 3; { int value = 4; } }
                return value;
            }
            """);

    private static void Check(string source) => Compiler.Check(source, options: new() { NoStdLib = true, NoBuiltins = true });
}
