namespace gflat.Tests;

public class StandardStringTests
{
    private static ExecutionResult Run(string source) => CompilerTestHelper.Run(InterpolationTests.Sources(source));

    [Fact]
    public void FailedAppendPreservesLengthCapacityContentsAndTerminator()
    {
        var result = Run("""
            using std;
            namespace Allocator {
                public replace void*? Allocate(nuint size) {
                    if (size == (nuint)7) return null;
                    return malloc(size);
                }
                public replace void Free(void* p) { free(p); }
            }
            int main() {
                String text = s"abc";
                nuint oldCapacity = text.Capacity;
                try { text.Append(text.Data, text.Length); return 1; } catch { }
                if (text.Length != (nuint)3 || text.Capacity != oldCapacity) return 2;
                if (text.Data[0] != 'a' || text.Data[2] != 'c' || text.Data[3] != '\0') return 3;
                text.Append(c"", 0);
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ClonesAndIgnoredFactoryResultsReleaseEachAllocationOnce()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char)* text, ...);
            namespace Allocator {
                public replace void*? Allocate(nuint size) { printf(c"A"); return malloc(size); }
                public replace void Free(void* p) { printf(c"F"); free(p); }
            }
            int main() {
                { String empty = new String(); empty.Clear(); }
                String::From(c"ignored");
                {
                    String original = s"a\0b";
                    String clone = original.Clone();
                    original[(nuint)2] = 'c';
                    if (clone.Length != (nuint)3 || clone.Data[2] != 'b' || clone.Data[3] != '\0') return 1;
                }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("AFAAFF", result.StandardOutput);
    }

    [Fact]
    public void FromCStringScansTerminatorWhileCountOverloadPreservesBytes()
    {
        var result = Run("""
            using std;
            int main() {
                String empty = String::From(c"");
                String text = String::From(c"hello");
                String truncated = String::From(c"ab\0cd");
                String counted = String::From(c"ab\0cd", 5);
                char[3] source = "ok";
                String copied = String::From(&source[0]);
                source[0] = 'X';
                if (empty.Length != (nuint)0 || text.Length != (nuint)5) return 1;
                if (truncated.Length != (nuint)2 || counted.Length != (nuint)5) return 2;
                if (counted.Data[3] != 'c' || copied.Data[0] != 'o') return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void OwnsCopiesGrowsAndAppendsItsOwnStorage()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char)* p, ...);
            int main() {
                char[3] input = "ab";
                String text = String::From(&input[0], 2);
                input[0] = 'z';
                text.Append(text.Data, text.Length);
                text.Reserve(32);
                text.Append(text.Data + 1, 3);
                String clone = text.Clone();
                text[(nuint)0] = 'X';
                if (clone.Equals(&text)) return 1;
                printf(c"%s|%s", text.Data, clone.Data);
                text.Clear();
                if (text.Length != (nuint)0 || text.Data[0] != '\0' || text.Capacity != (nuint)32) return 2;
                return 0;
            }
            """);
        Assert.Equal("Xbabbab|ababbab", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void EmptyStringsAndOwnedConversionsAreIndependent()
    {
        var result = Run("""
            using std;
            int main() {
                String empty = new String();
                empty.Clear();
                if (empty.Length != (nuint)0 || empty.Data[0] != '\0') return 1;
                String text = s"hello";
                char* owned = text.ToString();
                defer Allocator::Free((void*)owned);
                text[(nuint)0] = 'X';
                if (owned[0] != 'h') return 2;
                char* zero = empty.ToString();
                defer Allocator::Free((void*)zero);
                if (zero[0] != '\0') return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BoundsAndOverflowFailWithoutChangingString()
    {
        var result = Run("""
            using std;
            int main() {
                String text = s"abc";
                int caught = 0;
                try { text[(nuint)3]; } catch { caught++; }
                try { text[(nuint)3] = 'x'; } catch { caught++; }
                try { text.Reserve((nuint)0 - (nuint)1); } catch { caught++; }
                try { text.Append(c"x", (nuint)0 - (nuint)1); } catch { caught++; }
                if (caught != 4 || text.Length != (nuint)3 || text.Data[0] != 'a') return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AllocationFailurePreservesPreviousStorage()
    {
        var result = Run("""
            using std;
            namespace Allocator {
                public replace void*? Allocate(nuint size) { if (size == (nuint)101) return null; return malloc(size); }
                public replace void Free(void* p) { free(p); }
            }
            int main() {
                String text = s"abc";
                try { text.Reserve(100); return 1; } catch { }
                if (text.Length != (nuint)3 || text.Data[0] != 'a') return 2;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void IntegerOverloadsPreserveSignednessAndWidth()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char)* p, ...);
            int main() {
                String text = s$"{(byte)255} {(sbyte)-128} {(short)-32768} {(ushort)65535} {4294967295u} {(nint)-12} {(nuint)17} {0}";
                printf(c"%s", text.Data); return 0;
            }
            """);
        Assert.Equal("255 -128 -32768 65535 4294967295 -12 17 0", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }
}
