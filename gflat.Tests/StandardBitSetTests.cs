namespace gflat.Tests;

public sealed class StandardBitSetTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Collections/Enumerable.gf", "BitSet.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void WordBoundariesShrinkRegrowAndClonePreserveOnlyLiveBits()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                BitSet bits = new(65);
                if (bits.CountSetBits() != 0) return 1;
                bits[0] = true; bits[31] = true; bits[32] = true; bits[64] = true;
                if (bits.CountSetBits() != 4) return 2;
                if (!bits[0] || !bits[31] || !bits[32] || !bits[64] || bits[63]) return 3;
                bits.Resize(32); bits.Resize(65);
                if (bits.CountSetBits() != 2 || bits[32] || bits[64]) return 4;
                bits.Resize(31); bits.Resize(32);
                if (bits[31] || bits.CountSetBits() != 1) return 5;
                bits.SetAll(true);
                if (bits.CountSetBits() != 32) return 6;
                bits.Resize(33);
                if (bits[32] || bits.CountSetBits() != 32) return 7;
                BitSet copy = bits.Clone(); copy[0] = false;
                if (!bits[0] || copy[0]) return 8;
                bits.Clear(); bits.Flip(32);
                if (bits.CountSetBits() != 1 || !bits[32]) return 9;
                bits.Flip(32); bits.Resize(0);
                if (bits.Length != 0 || bits.CountSetBits() != 0) return 10;
                bits.Resize(97); bits.SetAll(true);
                if (bits.CountSetBits() != 97) return 11;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void SetOperationsAndEnumerationRespectLogicalLength()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                BitSet a = new(70); BitSet b = new(70);
                a[1] = true; a[32] = true; b[32] = true; b[69] = true;
                a.UnionWith(&b);
                if (a.CountSetBits() != 3) return 1;
                nuint sum = 0; foreach (nuint index in a) sum += index;
                if (sum != 102) return 2;
                a.IntersectWith(&b);
                if (a.CountSetBits() != 2 || a[1]) return 3;
                a.SymmetricExceptWith(&b);
                if (a.CountSetBits() != 0) return 4;
                a.SetAll(true); a.ExceptWith(&b);
                if (a.CountSetBits() != 68 || a[32] || a[69]) return 5;
                a.ExceptWith(&a);
                if (a.CountSetBits() != 0) return 6;
                BitSet::Enumerator iterator = b.GetEnumerator();
                int caught = 0;
                try { nuint ignored = iterator.Current; } catch (ref Exception error) { caught++; }
                if (!iterator.MoveNext() || iterator.Current != 32) return 7;
                if (!iterator.MoveNext() || iterator.Current != 69) return 8;
                if (iterator.MoveNext() || iterator.MoveNext()) return 9;
                try { nuint ignored = iterator.Current; } catch (ref Exception error) { caught++; }
                return caught - 2;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void InvalidIndicesAndMismatchedLengthsThrowWithoutChanges()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                BitSet a = new(33); BitSet b = new(32); a[32] = true; int caught = 0;
                try { a[33] = true; } catch (ref Exception error) { caught++; }
                try { bool ignored = a[33]; } catch (ref Exception error) { caught++; }
                try { a.Flip(33); } catch (ref Exception error) { caught++; }
                try { a.UnionWith(&b); } catch (ref Exception error) { caught++; }
                try { a.IntersectWith(&b); } catch (ref Exception error) { caught++; }
                try { a.ExceptWith(&b); } catch (ref Exception error) { caught++; }
                try { a.SymmetricExceptWith(&b); } catch (ref Exception error) { caught++; }
                if (!a[32] || a.CountSetBits() != 1) return 1;
                return caught - 7;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedResizePreservesBitsAndLength()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            extern void* malloc(nuint bytes);
            extern void free(void* address);
            namespace Memory {
                replace void*? Allocate(nuint bytes) { if (bytes > 4) return null; return malloc(bytes); }
                replace void Free(void* address) { free(address); }
            }
            int main() {
                BitSet bits = new(32); bits[31] = true;
                try { bits.Resize(33); return 1; } catch (ref BadAllocException error) {}
                if (bits.Length != 32 || bits.CountSetBits() != 1 || !bits[31]) return 2;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
