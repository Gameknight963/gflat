namespace gflat.Tests.Std.Text;

public sealed class LimitedWriterTests
{
    private const string SinkSource = """
        using Standard::Text;
        using Standard::IO;
        int destroyed = 0;
        struct Sink : ITextWriter
        {
            private char* storage;
            private nuint capacity;
            public nuint Length;
            public nuint Calls;
            public bool FailNext;
            public bool Closed;

            public Sink(char* data, nuint count)
            {
                storage = data;
                capacity = count;
                Length = 0;
                Calls = 0;
                FailNext = false;
                Closed = false;
            }

            public void Write(readonly(char)* data, nuint count)
            {
                Calls++;
                if (Closed) throw new IOException("Closed sink");
                if (count > capacity - Length) throw new Exception("Sink guard exceeded", 89);
                if (FailNext)
                {
                    FailNext = false;
                    if (count != 0) { storage[Length] = data[0]; Length++; }
                    throw new Exception("Original partial output", 83);
                }
                for (nuint index = 0; index < count; index++) storage[Length + index] = data[index];
                Length += count;
            }

            ~Sink() { destroyed++; }
        }
        """;

    private const string AllocationHooks = """
        int attempts = 0;
        namespace Memory
        {
            namespace Native
            {
                private extern void*? malloc(nuint count);
                private extern void free(void* storage);
            }
            public replace void*? Allocate(nuint count)
            {
                attempts++;
                return Native::malloc(count);
            }
            public replace void Free(void* storage) nothrow { Native::free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ChunkedBinaryFramesPreserveIndependentByteOraclesAndGuardsWithoutAllocating(int optimization)
    {
        byte[] bytes = Enumerable.Range(0, 257).Select(index => unchecked((byte)(index * 73 + 19))).ToArray();
        bytes[0] = 0;
        bytes[1] = 255;
        string payload = string.Join(", ", bytes.Select(value => "(char)" + value));
        Run(SinkSource + $$"""
            int main()
            {
                char[257] payload = [{{payload}}];
                char[257] expected = [{{payload}}];
                char* data = payload;
                nuint[7] lengths = [0, 1, 2, 7, 31, 64, 257];
                nuint[5] blocks = [1, 2, 5, 64, 1000];
                for (nuint row = 0; row < 7; row++)
                {
                    for (nuint column = 0; column < 5; column++)
                    {
                        char[263] storage;
                        for (nuint index = 0; index < 263; index++) storage[index] = '!';
                        Sink sink = new(&storage[2], lengths[row] + 1);
                        {
                            LimitedWriter frame = new(&sink, lengths[row]);
                            immutable(LimitedWriter)* view = &frame;
                            if (view.Remaining != lengths[row] || sink.Calls != 0) return 1;
                            frame.Write(((nuint)1).reinterpret<readonly(char)*>(), 0);
                            if (sink.Calls != 0) return 2;
                            nuint at = 0;
                            nuint calls = 0;
                            while (at < lengths[row])
                            {
                                nuint count = lengths[row] - at;
                                if (count > blocks[column]) count = blocks[column];
                                frame.Write(data + at, count);
                                calls++;
                                at += count;
                                if (view.Remaining != lengths[row] - at || sink.Calls != calls) return 3;
                            }
                            frame.Write(((nuint)1).reinterpret<readonly(char)*>(), 0);
                            if (view.Remaining != 0 || sink.Length != lengths[row] || sink.Calls != calls) return 4;
                        }
                        if (sink.Closed || sink.Length != lengths[row]) return 5;
                        for (nuint index = 0; index < lengths[row]; index++)
                            if (storage[index + 2] != expected[index] || payload[index] != expected[index]) return 6;
                        sink.Write("M", 1);
                        if (storage[lengths[row] + 2] != 'M') return 7;
                        for (nuint index = 0; index < 2; index++) if (storage[index] != '!') return 8;
                        for (nuint index = lengths[row] + 3; index < 263; index++) if (storage[index] != '!') return 9;
                    }
                }
                if (destroyed != 35 || attempts != 0) return 10;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PreflightRejectionAllowsRetryButSinkErrorsPreserveOriginalExceptionAndPoison(int optimization)
    {
        Run(SinkSource + """
            int main()
            {
                readonly(char)* missing = ((nuint)1).reinterpret<readonly(char)*>();
                nuint maximum = (nuint)0 - 1;
                char[12] storage;
                for (nuint index = 0; index < 12; index++) storage[index] = '!';
                Sink sink = new(&storage[1], 10);
                LimitedWriter frame = new(&sink, 3);
                frame.Write("A", 1);
                bool caught = false;
                try { frame.Write(missing, 3); }
                catch (ref IOException error) { caught = true; }
                if (!caught || frame.Remaining != 2 || sink.Calls != 1 || sink.Length != 1) return 20;
                caught = false;
                try { frame.Write(missing, maximum); }
                catch (ref IOException error) { caught = true; }
                if (!caught || frame.Remaining != 2 || sink.Calls != 1 || sink.Length != 1) return 21;
                char high = (char)255;
                frame.Write("\0", 1);
                frame.Write(&high, 1);
                frame.Write(missing, 0);
                if (frame.Remaining != 0 || sink.Length != 3 || sink.Calls != 3) return 22;
                if (storage[0] != '!' || storage[1] != 'A' || storage[2] != '\0' || (byte)storage[3] != (byte)255 || storage[4] != '!') return 23;

                LimitedWriter partial = new(&sink, 4);
                sink.FailNext = true;
                caught = false;
                try { partial.Write("BCD", 3); }
                catch (ref Exception error)
                {
                    caught = error.GetCode() == 83 && CString::Equals(error.GetMessage(), "Original partial output");
                }
                if (!caught || partial.Remaining != 4 || sink.Calls != 4 || sink.Length != 4 || storage[4] != 'B') return 24;
                int rejected = 0;
                try { partial.Write("R", 1); }
                catch (ref IOException error) { rejected++; }
                try { partial.Write(missing, 0); }
                catch (ref IOException error) { rejected++; }
                if (rejected != 2 || partial.Remaining != 4 || sink.Calls != 4 || sink.Length != 4) return 25;

                sink.Closed = true;
                LimitedWriter closed = new(&sink, 1);
                closed.Write(missing, 0);
                if (sink.Calls != 4) return 26;
                caught = false;
                try { closed.Write("Z", 1); }
                catch (ref IOException error) { caught = true; }
                if (!caught || closed.Remaining != 1 || sink.Calls != 5) return 27;
                caught = false;
                try { closed.Write(missing, 0); }
                catch (ref IOException error) { caught = true; }
                if (!caught || sink.Calls != 5) return 28;

                CountingWriter counter = new();
                LimitedWriter huge = new(&counter, maximum);
                int before = attempts;
                huge.Write(missing, maximum);
                huge.Write(missing, 0);
                if (huge.Remaining != 0 || counter.Length != maximum || attempts != before) return 29;
                caught = false;
                try { huge.Write(missing, 1); }
                catch (ref IOException error) { caught = true; }
                if (!caught || counter.Length != maximum || huge.Remaining != 0) return 30;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BufferedAcceptanceCopiesAndNestedAllowancesDoNotOwnFlushOrCloseTheSink(int optimization)
    {
        Run(SinkSource + """
            int main()
            {
                {
                    char[24] storage;
                    for (nuint index = 0; index < 24; index++) storage[index] = '!';
                    Sink sink = new(&storage[1], 22);
                    char[4] scratch;
                    BufferedWriter buffered = new(&sink, scratch, 4);
                    {
                        LimitedWriter accepted = new(&buffered, 3);
                        accepted.Write("ABC", 3);
                        if (accepted.Remaining != 0 || sink.Calls != 0 || sink.Length != 0) return 40;
                    }
                    if (sink.Calls != 0 || sink.Closed || destroyed != 0) return 41;
                    buffered.Flush();
                    if (sink.Calls != 1 || sink.Length != 3) return 42;
                    {
                        LimitedWriter original = new(&sink, 2);
                        LimitedWriter duplicate = original;
                        original.Write("xy", 2);
                        duplicate.Write("z", 1);
                        if (original.Remaining != 0 || duplicate.Remaining != 1 || sink.Length != 6) return 43;
                    }
                    {
                        LimitedWriter outer = new(&sink, 3);
                        LimitedWriter inner = new(&outer, 2);
                        inner.Write("uv", 2);
                        if (outer.Remaining != 1 || inner.Remaining != 0 || sink.Length != 8) return 44;
                        bool caught = false;
                        try { inner.Write("!", 1); }
                        catch (ref IOException error) { caught = true; }
                        if (!caught || outer.Remaining != 1 || sink.Length != 8) return 45;
                        outer.Write("w", 1);
                        if (outer.Remaining != 0 || sink.Length != 9) return 46;
                    }
                    if (sink.Closed || destroyed != 0) return 47;
                    sink.Write("M", 1);
                    readonly(char)* expected = "ABCxyzuvwM";
                    if (sink.Length != 10) return 48;
                    for (nuint index = 0; index < 10; index++) if (storage[index + 1] != expected[index]) return 49;
                    if (storage[0] != '!' || storage[11] != '!' || storage[23] != '!') return 50;
                }
                if (destroyed != 1) return 51;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CaughtReentrantRejectionsDoNotClearThePendingOuterWriteState(int optimization)
    {
        Run("""
            using Standard::Text;
            using Standard::IO;
            struct ReentrantSink : ITextWriter
            {
                public ITextWriter*? Owner;
                public int Calls;
                public int Rejected;
                public nuint Accepted;

                public ReentrantSink()
                {
                    Owner = null;
                    Calls = 0;
                    Rejected = 0;
                    Accepted = 0;
                }

                public void Write(readonly(char)* data, nuint count)
                {
                    Calls++;
                    if (Calls > 2) throw new Exception("Unexpected recursive output", 87);
                    try { Owner!.Write(((nuint)1).reinterpret<readonly(char)*>(), 0); }
                    catch (ref IOException error) { Rejected++; }
                    try { Owner!.Write("R", 1); }
                    catch (ref IOException error) { Rejected++; }
                    Accepted += count;
                }
            }
            int main()
            {
                ReentrantSink sink = new();
                LimitedWriter frame = new(&sink, 5);
                sink.Owner = &frame;
                frame.Write("A", 1);
                if (frame.Remaining != 4 || sink.Accepted != 1 || sink.Calls != 1 || sink.Rejected != 2) return 60;
                frame.Write("BC", 2);
                if (frame.Remaining != 2 || sink.Accepted != 3 || sink.Calls != 2 || sink.Rejected != 4) return 61;
                return 0;
            }
            """, optimization);
    }

    [Fact]
    public void TheAdapterChecksWithOnlyItsExplicitCoreDependenciesAndHostExceptionStorage()
    {
        string[] names = ["core/ITextWriter.gf", "core/Exception.gf", "core/IO/IOException.gf", "core/Text/LimitedWriter.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("minimal-limited-writer.gf", """
                using Standard::Text;
                namespace Memory
                {
                    private extern void*? HostAllocate(nuint count);
                    private extern void HostFree(void* storage);
                    public void*? Allocate(nuint count) => HostAllocate(count);
                    public void Free(void* storage) nothrow { HostFree(storage); }
                }
                struct Sink : ITextWriter
                {
                    public void Write(readonly(char)* data, nuint count) { }
                }
                int main()
                {
                    Sink sink = new();
                    LimitedWriter frame = new(&sink, 1);
                    frame.Write("X", 1);
                    return (int)frame.Remaining;
                }
                """));
        Compiler.Check(sources, options: new() { NoStdLib = true, NoBuiltins = true });
    }

    private static void Run(string program, int optimization)
    {
        string[] names = ["core/Exception.gf", "core/ITextWriter.gf", "core/IO/IOException.gf",
            "core/Text/LimitedWriter.gf", "core/Text/BufferedWriter.gf", "core/Text/CountingWriter.gf",
            "core/Text/CString.gf", "core/Text/BufferTooSmallException.gf",
            "libc/Memory/allocation.gf", "libc/Exceptions/unhandled.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("limited-writer-tests.gf", program))
            .Append(new SourceFile("limited-writer-allocation-hooks.gf", AllocationHooks));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
