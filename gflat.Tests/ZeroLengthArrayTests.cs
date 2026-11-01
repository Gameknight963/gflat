using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ZeroLengthArrayTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyBuffersKeepElementAlignmentAndParticipateInOrdinaryValueOperations(int optimization)
        => Run("""
            int[0] Identity(int[0] values) nothrow => values;
            struct Packet { public byte First; public long[0] Empty; public byte Last; }
            int main() nothrow {
                int[0] values = [];
                int[] inferred = [];
                int[0] copied = Identity(values);
                copied = values;
                int[0][2] rows = [[], []];
                if (sizeof(values) != 0 || sizeof(inferred) != 0 || sizeof(rows) != 0) return 1;
                if (alignof(int[0]) != alignof(int) || alignof(long[0]) != alignof(long)) return 2;
                Packet packet = new();
                nuint start = (&packet).reinterpret<nuint>();
                if ((&packet.Last).reinterpret<nuint>() - start != (nuint)alignof(long)) return 3;
                if (sizeof(Packet) != 2 * alignof(long)) return 4;
                foreach (int item in copied) { return 5; }
                foreach (int[0] row in rows) { if (sizeof(row) != 0) return 6; }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyResourceBuffersRequireNeitherElementCopiesNorElementDestruction(int optimization)
        => Run("""
            int destroyed = 0;
            int visited = 0;
            struct Resource { public ~Resource() { destroyed++; } }
            Resource[0] Produce() nothrow {
                visited++;
                Resource[0] result = [];
                return result;
            }
            int main() nothrow {
                {
                    Resource[0] first;
                    Resource[0] second = first;
                    second = first;
                    foreach (Resource item in Produce()) { return 10; }
                    static if (reflect(Resource[0]).HasDestructor) { missingCleanup(); }
                }
                if (destroyed != 0 || visited != 1) return 11;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyConstantBuffersSupportConstructionDecayAndBothIterationForms(int optimization)
        => Run("""
            const int[0] Build() nothrow { int[0] scratch; return scratch; }
            const int[0] Empty = Build();
            const readonly(int)* View() nothrow { int[0] local = []; return local; }
            const readonly(int)* Pointer = View();
            const int Count() nothrow {
                int count = 0;
                foreach (int item in Empty) { count++; }
                return count;
            }
            const int Iterations = Count();
            int main() nothrow {
                static foreach (int item in Empty) { missingElement(); }
                static foreach (reflection field in reflect(int).Fields) { missingField(); }
                int[0] copied = Build();
                readonly(int)* decayed = Empty;
                if (sizeof(copied) != 0 || Iterations != 0) return 20;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData("int[-1] values;")]
    [InlineData("void[0] values;")]
    [InlineData("Missing[0] values;")]
    [InlineData("int[][0] values;")]
    [InlineData("int[1] values = [];")]
    public void EmptyBuffersStillRequireValidCompleteElementsAndMatchingShapes(string declaration)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { " + declaration + " return 0; }"));

    [Fact]
    public void EmptyInlineEdgesStillRejectRecursiveValueLayouts()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("struct Recursive { public Recursive[0] Value; } int main() => 0;"));

    [Fact]
    public void EmptyConstantBufferCannotBeDereferenced()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("const int[0] Empty=[]; const int Read=Empty[0]; int main()=>Read;"));

    private static void Run(string source, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(source, optimization).ExitCode);
}
