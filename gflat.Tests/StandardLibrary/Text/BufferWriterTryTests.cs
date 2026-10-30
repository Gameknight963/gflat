namespace gflat.Tests.Std.Text;

public sealed class BufferWriterTryTests
{
    private static void Run(string program, int optimization)
    {
        var sources = new[] { "Text/BufferTooSmallException.gf", "Text/BufferWriter.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
            .Append(new SourceFile("main.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverflowIsAtomicAndExactCapacityPreservesEmbeddedZerosAndGuards(int optimization)
    {
        Run("""
            using Standard; using Standard::Text;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint count) { Calls::Allocations++; return null; }
                public replace void Free(void* storage) nothrow { }
            }
            int main() {
                char[12] storage; char[12] before;
                for (nuint i = 0; i < 12; i++) storage[i] = '!';
                BufferWriter writer = new(&storage[1], 10);
                if (!writer.TryWrite("A\0B", 3) || writer.Length != 3 || writer.Remaining != 6) return 1;
                for (nuint i = 0; i < 12; i++) before[i] = storage[i];
                if (writer.TryWrite("1234567", 7) || writer.TryWrite("1234567")) return 2;
                if (writer.TryWrite(((nuint)1).reinterpret<readonly(char)*>(), (nuint)0 - 1)) return 3;
                for (nuint i = 0; i < 12; i++) if (storage[i] != before[i]) return 4;
                if (writer.Length != 3 || writer.Remaining != 6) return 5;
                if (!writer.TryWrite("cdefgh") || writer.Length != 9 || writer.Remaining != 0) return 6;
                if (storage[1] != 'A' || storage[2] != '\0' || storage[3] != 'B' || storage[10] != '\0') return 7;
                for (nuint i = 0; i < 6; i++) if (storage[i + 4] != "cdefgh"[i]) return 8;
                if (storage[0] != '!' || storage[11] != '!') return 9;
                char*? absent = null;
                if (!writer.TryWrite(absent!, 0) || !writer.TryWrite("") || writer.TryWrite("x")) return 10;
                if (writer.Length != 9 || storage[10] != '\0' || Calls::Allocations != 0) return 11;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndTerminatorOnlyStorageNeedNoOutputAccessForEmptyWrites(int optimization)
    {
        Run("""
            using Standard; using Standard::Text;
            bool TryOnly(char* storage) nothrow {
                BufferWriter writer = new(storage, 0);
                char*? absent = null;
                return writer.TryWrite(absent!, 0) && writer.TryWrite("") && !writer.TryWrite("x")
                    && writer.Length == 0 && writer.Remaining == 0;
            }
            int main() {
                char*? absent = null;
                if (!TryOnly(absent!)) return 1;
                char sentinel = '!';
                BufferWriter zero = new(&sentinel, 0);
                if (!zero.TryWrite("", 0) || zero.TryWrite("x", 1) || sentinel != '!') return 2;
                BufferWriter one = new(&sentinel, 1);
                if (!one.TryWrite("", 0) || !one.TryWrite("") || one.TryWrite("x") || sentinel != '\0') return 3;
                if (one.Length != 0 || one.Capacity != 0) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlapsIncludeTerminatorAndInitializedTailAfterClear(int optimization)
    {
        Run("""
            using Standard; using Standard::Text;
            int main() {
                char[18] storage;
                for (nuint i = 0; i < 18; i++) storage[i] = '!';
                BufferWriter writer = new(&storage[1], 16);
                if (!writer.TryWrite("abc")) return 1;
                // Count the original terminator as payload before appending a new one.
                if (!writer.TryWrite(writer.Data, 4) || writer.Length != 7) return 2;
                for (nuint i = 0; i < 7; i++) if (writer.Data[i] != "abcabc\0"[i]) return 3;
                if (writer.Data[7] != '\0') return 4;
                writer.Clear();
                if (!writer.TryWrite("abcdef")) return 5;
                writer.Clear();
                // Clear only changes byte zero; cdef and its terminator remain initialized.
                if (!writer.TryWrite(&storage[3]) || writer.Length != 4) return 6;
                for (nuint i = 0; i < 4; i++) if (writer.Data[i] != "cdef"[i]) return 7;
                if (writer.Data[4] != '\0') return 8;
                // CString length must be measured before overwriting its terminator.
                if (!writer.TryWrite(writer.Data) || writer.Length != 8) return 9;
                for (nuint i = 0; i < 8; i++) if (writer.Data[i] != "cdefcdef"[i]) return 10;
                if (writer.Data[8] != '\0' || storage[0] != '!' || storage[17] != '!') return 11;
                // Force backwards memmove for a source spanning the destination start.
                writer.Clear();
                if (!writer.TryWrite("ab")) return 12;
                storage[3] = 'c'; storage[4] = 'd'; storage[5] = 'e'; storage[6] = 'f';
                if (!writer.TryWrite(writer.Data, 6) || writer.Length != 8) return 13;
                for (nuint i = 0; i < 8; i++) if (writer.Data[i] != "ababcdef"[i]) return 14;
                if (writer.Data[8] != '\0' || storage[0] != '!' || storage[17] != '!') return 15;
                return 0;
            }
            """, optimization);
    }
}
