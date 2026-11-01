namespace gflat.Tests.Std.IO;

public sealed class MemoryStreamTests
{
    private const string Imports = """
        using Standard;
        using Standard::Collections;
        using Standard::IO;
        using Standard::Memory;

        """ + "\n";

    private const string Contents = """
        bool Contents(readonly(MemoryStream)* stream, readonly(char)* expected, nuint count) {
            if (stream.Length != (long)count) return false;
            Span<readonly(char)> view = stream.AsReadOnly();
            for (nuint index = 0; index < count; index++) if (view.Data![index] != expected[index]) return false;
            return true;
        }
        """;

    private const string AllocationTracking = """
        extern void*? malloc(nuint bytes);
        extern void free(void* memory);
        static class State { public static bool Failing; public static int Live; }
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                if (State::Failing) return null;
                void*? result = malloc(bytes);
                if (result != null) State::Live++;
                return result;
            }
            public replace void Free(void* memory) nothrow { State::Live--; free(memory); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReaderAndWriterInterfacesPreserveAllByteValuesAndZeroCountOperations(int optimization)
        => Run(Imports + Contents + """
            int main() {
                MemoryStream stream = new();
                if (stream.Length != 0l || stream.Position != 0l || stream.Capacity != 0) return 1;
                if (!stream.AsSpan().IsEmpty || !stream.AsReadOnly().IsEmpty || stream.ReadByte() != -1 || stream.PeekByte() != -1) return 2;
                ITextWriter* writer = &stream;
                IByteReader* reader = &stream;
                char[5] input = ['A', '\0', (char)255, 'B', 'C'];
                writer.Write(input, 5);
                if (stream.Length != 5l || stream.Position != 5l) return 3;
                stream.Position = 0l;
                char[4] output = ['!', '!', '!', '!'];
                if (reader.Read(output, 3) != 3 || output[0] != 'A' || output[1] != '\0' || (byte)output[2] != (byte)255 || output[3] != '!') return 4;
                if (stream.PeekByte() != 66 || stream.Position != 3l || stream.ReadByte() != 66 || stream.ReadByte() != 67 || stream.ReadByte() != -1) return 5;
                stream.Position = 2l;
                if (stream.PeekByte() != 255 || stream.ReadByte() != 255) return 6;
                stream.Position = 9l;
                nuint capacity = stream.Capacity;
                if (reader.Read(((nuint)1).reinterpret<char*>(), 0) != 0) return 7;
                writer.Write(((nuint)1).reinterpret<readonly(char)*>(), 0);
                stream.Flush();
                if (stream.Position != 9l || stream.Length != 5l || stream.Capacity != capacity) return 8;
                stream.WriteByte('Z');
                if (stream.Length != 10l || stream.Position != 10l) return 9;
                Span<char> bytes = stream.AsSpan();
                for (nuint index = 5; index < 9; index++) if (bytes[index] != '\0') return 10;
                bytes[9] = 'Q';
                stream.Position = 9l;
                if (stream.ReadByte() != 81) return 11;
                stream.Clear();
                if (stream.Length != 0l || stream.Position != 0l || stream.Capacity != capacity || !stream.AsReadOnly().IsEmpty) return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SeekingAndResizingPreserveCursorAndZeroFillTruncatedRegions(int optimization)
        => Run(Imports + Contents + """
            int main() {
                MemoryStream stream = new MemoryStream(4);
                if (stream.Capacity < 4 || stream.Length != 0l || stream.Position != 0l) return 20;
                stream.Write("abcdef", 6);
                if (stream.Seek(-3l, SeekOrigin::Current) != 3l || stream.Seek(-1l, SeekOrigin::End) != 5l) return 21;
                stream.SetLength(2l);
                if (stream.Position != 5l || stream.Length != 2l || stream.ReadByte() != -1) return 22;
                stream.SetLength(6l);
                if (stream.Position != 5l || stream.Length != 6l || stream.ReadByte() != 0) return 23;
                if (!Contents(&stream, "ab\0\0\0\0", 6)) return 24;
                stream.Position = 1l;
                stream.Write("XYZ", 3);
                if (!Contents(&stream, "aXYZ\0\0", 6) || stream.Position != 4l) return 25;
                nuint before = stream.Capacity;
                stream.Reserve(before + 7);
                if (stream.Capacity < before + 7 || stream.Length != 6l || stream.Position != 4l || !Contents(&stream, "aXYZ\0\0", 6)) return 26;
                stream.SetLength(0l);
                if (stream.Length != 0l || stream.Position != 4l) return 27;
                stream.WriteByte((char)255);
                if (stream.Length != 5l) return 28;
                Span<readonly(char)> gap = stream.AsReadOnly();
                for (nuint index = 0; index < 4; index++) if (gap[index] != '\0') return 28;
                stream.Position = 4l;
                if (stream.ReadByte() != 255) return 29;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelfSourceWritesRebaseAcrossGrowthAndCopyBothOverlapDirections(int optimization)
        => Run(Imports + Contents + """
            int main() {
                MemoryStream stream = new MemoryStream(4);
                stream.Write("abcd", 4);
                Span<readonly(char)> original = stream.AsReadOnly();
                stream.Position = 2l;
                stream.Write(original.Data!, 4);
                if (!Contents(&stream, "ababcd", 6) || stream.Position != 6l) return 30;
                Span<readonly(char)> grown = stream.AsReadOnly();
                stream.Position = 0l;
                stream.Write(grown.Data! + 2, 4);
                if (!Contents(&stream, "abcdcd", 6) || stream.Position != 4l) return 31;
                stream.Position = 1l;
                stream.Write(grown.Data!, 5);
                if (!Contents(&stream, "aabcdc", 6) || stream.Position != 6l) return 32;
                stream.Position = 3l;
                stream.Write(grown.Data!, 3);
                if (!Contents(&stream, "aabaab", 6)) return 33;
                stream.Position = 10l;
                Span<readonly(char)> prefix = stream.AsReadOnly();
                stream.Write(prefix.Data!, 3);
                if (stream.Length != 13l || stream.Position != 13l) return 34;
                Span<readonly(char)> result = stream.AsReadOnly();
                for (nuint index = 6; index < 10; index++) if (result[index] != '\0') return 35;
                if (result[10] != 'a' || result[11] != 'a' || result[12] != 'b') return 36;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesOwnIndependentBuffersAndMovesTransferCleanup(int optimization)
        => Run(Imports + Contents + AllocationTracking + """
            int main() {
                {
                    MemoryStream empty = new();
                    empty.Position = 9l;
                    MemoryStream emptyCopy = empty;
                    if (emptyCopy.Length != 0l || emptyCopy.Capacity != 0 || emptyCopy.Position != 9l) return 39;
                    MemoryStream first = new MemoryStream(4);
                    first.Write("abc", 3);
                    first.Position = 8l;
                    MemoryStream second = first;
                    if (second.Position != 8l || !Contents(&second, "abc", 3) || first.AsReadOnly().Data == second.AsReadOnly().Data) return 40;
                    first.Position = 0l;
                    first.WriteByte('X');
                    if (!Contents(&second, "abc", 3)) return 41;
                    MemoryStream transferred = move first;
                    first = new MemoryStream();
                    if (first.Length != 0l || first.Capacity != 0 || transferred.Position != 1l || !Contents(&transferred, "Xbc", 3)) return 42;
                    MemoryStream* heap = new* MemoryStream();
                    heap.Write("own", 3);
                    MemoryStream copied = *heap;
                    delete heap;
                    if (!Contents(&copied, "own", 3)) return 43;
                }
                if (State::Live != 0) return 44;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidRangesAndZeroWritesAtThePositionLimitPreserveState(int optimization)
        => Run(Imports + Contents + """
            int main() {
                MemoryStream stream = new MemoryStream(4);
                stream.Write("abcd", 4);
                try { stream.Position = -1l; return 50; } catch (IOException* error) {}
                try { stream.Seek(-1l, SeekOrigin::Begin); return 51; } catch (IOException* error) {}
                try { stream.Seek(0l, (SeekOrigin)99); return 52; } catch (IOException* error) {}
                try { stream.SetLength(-1l); return 53; } catch (IOException* error) {}
                if (stream.Position != 4l || !Contents(&stream, "abcd", 4)) return 54;
                stream.Position = 9223372036854775807l;
                try { stream.Seek(1l, SeekOrigin::Current); return 55; } catch (IOException* error) {}
                try { stream.Write(((nuint)1).reinterpret<readonly(char)*>(), 1); return 56; } catch (IOException* error) {}
                stream.Write(((nuint)1).reinterpret<readonly(char)*>(), 0);
                if (stream.Position != 9223372036854775807l || !Contents(&stream, "abcd", 4)) return 57;
                if (stream.Seek(-9223372036854775807l, SeekOrigin::Current) != 0l) return 58;
                try { stream.Seek(-9223372036854775807l - 1l, SeekOrigin::Current); return 59; } catch (IOException* error) {}
                try { stream.Write(((nuint)1).reinterpret<readonly(char)*>(), (nuint)0 - (nuint)1); return 60; } catch (IOException* error) {}
                if (sizeof(nuint) == 8) {
                    try { stream.Reserve((nuint)0 - (nuint)1); return 61; } catch (IOException* error) {}
                }
                if (stream.Position != 0l || !Contents(&stream, "abcd", 4)) return 62;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllocationFailuresPreserveContentsCursorCapacityAndAssignmentTargets(int optimization)
        => Run(Imports + Contents + AllocationTracking + """
            int main() {
                {
                    MemoryStream source = new MemoryStream(4);
                    source.Write("abcd", 4);
                    source.Position = 2l;
                    MemoryStream destination = new();
                    destination.Write("kept", 4);
                    Span<readonly(char)> before = source.AsReadOnly();
                    Span<readonly(char)> destinationBefore = destination.AsReadOnly();
                    nuint capacity = source.Capacity;
                    int live = State::Live;
                    State::Failing = true;
                    try { source.Write(before.Data!, 4); return 70; } catch (BadAllocException* error) {}
                    try { source.SetLength(10l); return 71; } catch (BadAllocException* error) {}
                    try { source.Reserve(100); return 72; } catch (BadAllocException* error) {}
                    try { destination = source; return 73; } catch (BadAllocException* error) {}
                    try { MemoryStream failed = new MemoryStream(4); return 78; } catch (BadAllocException* error) {}
                    State::Failing = false;
                    if (source.Position != 2l || source.Capacity != capacity || source.AsReadOnly().Data != before.Data || !Contents(&source, "abcd", 4)) return 74;
                    if (destination.Position != 4l || destination.AsReadOnly().Data != destinationBefore.Data || !Contents(&destination, "kept", 4)) return 75;
                    if (State::Live != live) return 76;
                }
                if (State::Live != 0) return 77;
                return 0;
            }
            """, optimization);

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/IO/IOException.gf", "core/IO/IByteReader.gf", "core/IO/SeekOrigin.gf", "core/IO/MemoryStream.gf",
            "core/Collections/Vector.gf", "core/Collections/vector_resize.gf", "core/Collections/IEnumerable.gf",
            "core/Memory/Span.gf", "core/Queries/Enumerable.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("memory-stream.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
