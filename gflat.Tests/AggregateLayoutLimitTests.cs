using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class AggregateLayoutLimitTests
{
    [Theory]
    [InlineData("struct Huge { public byte[1073741824] a; public byte[1073741824] b; }")]
    [InlineData("struct Huge { public byte[2147483647] bytes; public int tail; }")]
    [InlineData("struct Huge { public int first; public byte[2147483643] bytes; }")]
    [InlineData("class Huge { public byte[2147483640] bytes; }")]
    [InlineData("class Huge { public byte[2147483633] bytes; }")]
    [InlineData("class Base { public byte[1073741824] a; } class Huge : Base { public byte[1073741824] b; }")]
    public void SizeofRejectsAggregateOverflowIncludingAlignment(string declaration)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            declaration + " int main() => (int)sizeof(Huge);"));
        Assert.Contains("layout exceeds the supported size", error.Description);
        Assert.True(error.Line > 0);
    }

    [Theory]
    [InlineData("new Huge()", "Huge")]
    [InlineData("new* Huge()", "Huge*")]
    [InlineData("new^ Huge()", "Huge^")]
    public void ConstructionRejectsOversizedAggregateWithoutAllocatingIt(string creation, string type)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.EmitIr("""
            alias GC = TestCollector;
            static class TestCollector {
                public static void* Allocate(nuint bytes, nuint alignment) => ((nuint)1).reinterpret<void*>();
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            struct Huge { public byte[1073741824] a; public byte[1073741824] b; }
            """ + " int main() { " + type + " value = " + creation + "; return 0; }"));
        Assert.Contains("layout exceeds the supported size", error.Description);
    }

    [Theory]
    [InlineData("struct Huge { public byte[2147483647] bytes; }", 2147483647)]
    [InlineData("struct Huge { public int first; public byte[2147483640] bytes; }", 2147483644)]
    [InlineData("class Huge { public byte[2147483632] bytes; }", 2147483640)]
    public void RepresentableLayoutsNearLimitRemainValid(string declaration, int size)
    {
        var (_, checker) = Compiler.Check(declaration);
        var actual = checker.GetStruct("Huge") is { } structure
            ? checker.GetStructSize(structure)
            : checker.GetClassSize(checker.GetClass("Huge")!);
        Assert.Equal(size, actual);
    }

    [Fact]
    public void PointersDoNotRequireRepresentablePointeeLayout()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Huge { public byte[1073741824] a; public byte[1073741824] b; }
            int main() {
                Huge*? value = null;
                if (sizeof(Huge*) != sizeof(void*) || value != null) return 1;
                return 0;
            }
            """).ExitCode);
}
