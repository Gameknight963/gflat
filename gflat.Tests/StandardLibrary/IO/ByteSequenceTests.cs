namespace gflat.Tests.Std.IO;

public sealed class ByteSequenceTests
{
    private const string Reader = """
        using Standard::IO;
        struct Source : IByteReader
        {
            public nuint Length;
            public nuint Position;
            public nuint Calls;
            public bool FailNext;
            public bool OverreportNext;
            public bool Closed;

            public Source(nuint length)
            {
                Length = length;
                Position = 0;
                Calls = 0;
                FailNext = false;
                OverreportNext = false;
                Closed = false;
            }

            public nuint Read(char* output, nuint count)
            {
                Calls++;
                if (Closed) throw new IOException("Closed source");
                if (count != 1) throw new Exception("Unexpected request", 72);
                if (FailNext)
                {
                    FailNext = false;
                    Position++;
                    throw new Exception("Consumed byte before failure", 73);
                }
                if (OverreportNext)
                {
                    OverreportNext = false;
                    return 2;
                }
                if (Position == Length) return 0;
                output[0] = (char)(byte)Position;
                Position++;
                return 1;
            }
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
    public void RestrictedViewsObserveCachedBytesWithoutReadingOrChangingTheSource(int optimization)
    {
        Run(Reader + """
            int main()
            {
                Source source = new(3);
                ByteSequence sequence = EnumerateBytes(&source);
                readonly(ByteSequence)* sequenceView = &sequence;
                ByteSequence::Enumerator iterator = sequenceView.GetEnumerator();
                readonly(ByteSequence::Enumerator)* shallow = &iterator;
                immutable(ByteSequence::Enumerator)* deep = &iterator;
                if (source.Calls != 0 || attempts != 0) return 1;
                bool caught = false;
                try { shallow.Current; } catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 0) return 2;
                caught = false;
                try { deep.Current; } catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 0) return 3;
                int before = attempts;
                for (nuint index = 0; index < 3; index++)
                {
                    if (!iterator.MoveNext()) return 4;
                    if (shallow.Current != (byte)index || deep.Current != (byte)index || deep.Current != (byte)index) return 5;
                    if (source.Calls != index + 1 || source.Position != index + 1 || attempts != before) return 6;
                }
                if (iterator.MoveNext() || iterator.MoveNext() || source.Calls != 4) return 7;
                caught = false;
                try { deep.Current; } catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 4) return 8;
                caught = false;
                try { shallow.Current; } catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 4) return 9;

                Source failing = new(3);
                ByteSequence::Enumerator failed = EnumerateBytes(&failing).GetEnumerator();
                immutable(ByteSequence::Enumerator)* failedView = &failed;
                if (!failed.MoveNext() || failedView.Current != (byte)0) return 10;
                failing.FailNext = true;
                caught = false;
                try { failed.MoveNext(); } catch (ref Exception error) { caught = error.code == 73; }
                if (!caught || failing.Calls != 2) return 11;
                caught = false;
                try { failedView.Current; } catch (ref IOException error) { caught = true; }
                if (!caught || failing.Calls != 2) return 12;
                if (!failed.MoveNext() || failedView.Current != (byte)2 || failing.Calls != 3) return 13;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData("readonly(ByteSequence::Enumerator)", "MoveNext", "non-readonly")]
    [InlineData("immutable(ByteSequence::Enumerator)", "MoveNext", "non-immutable")]
    [InlineData("immutable(ByteSequence)", "GetEnumerator", "non-immutable")]
    public void RestrictedViewsCannotAdvanceOrExposeAMutableSource(string receiver, string member, string diagnostic)
    {
        string[] names = ["core/IO/IByteReader.gf", "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf",
            "core/IO/byte_reader.gf", "core/IO/ByteSequence.gf", "core/Collections/IEnumerable.gf", "core/Collections/IEnumerator.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("restricted-byte-sequence.gf",
                $"using Standard::IO; void Inspect({receiver}* value) {{ value.{member}(); }} int main() => 0;"));
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() =>
            Compiler.Check(sources, options: new() { NoStdLib = true }));
        Assert.Contains($"{diagnostic} method '{member}'", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DeferredUnsignedBytesAndIteratorStateNeverReadAheadOrAllocate(int optimization)
    {
        Run(Reader + """
            int main()
            {
                Source source = new(256);
                IByteReader* input = &source;
                ByteSequence sequence = input.EnumerateBytes();
                ByteSequence::Enumerator iterator = sequence.GetEnumerator();
                if (source.Calls != 0 || attempts != 0) return 1;
                bool caught = false;
                try { iterator.Current; }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 0) return 2;
                int before = attempts;
                for (nuint index = 0; index < 256; index++)
                {
                    if (!iterator.MoveNext() || iterator.Current != (byte)index) return 3;
                    if (iterator.Current != (byte)index || source.Calls != index + 1) return 4;
                }
                if (iterator.MoveNext() || iterator.MoveNext() || source.Calls != 257) return 5;
                if (attempts != before || source.Position != 256) return 6;
                caught = false;
                try { iterator.Current; }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 257) return 7;

                Source empty = new(0);
                ByteSequence::Enumerator end = EnumerateBytes(&empty).GetEnumerator();
                before = attempts;
                if (empty.Calls != 0 || end.MoveNext() || end.MoveNext() || empty.Calls != 1) return 8;
                if (attempts != before) return 9;

                Source shared = new(4);
                ByteSequence first = EnumerateBytes(&shared);
                ByteSequence duplicate = first;
                ByteSequence::Enumerator left = first.GetEnumerator();
                ByteSequence::Enumerator right = duplicate.GetEnumerator();
                if (shared.Calls != 0 || !left.MoveNext() || left.Current != (byte)0) return 10;
                if (!right.MoveNext() || right.Current != (byte)1 || left.Current != (byte)0) return 11;
                ByteSequence::Enumerator copied = left;
                if (!copied.MoveNext() || copied.Current != (byte)2 || left.Current != (byte)0) return 12;
                if (!left.MoveNext() || left.Current != (byte)3 || shared.Position != 4) return 13;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ForeachEarlyBreakAndFilteredTakeLeaveTheNextByteUnread(int optimization)
    {
        Run("using Standard::Queries;\n" + Reader + """
            bool Even(byte value) => value % (byte)2 == (byte)0;
            int main()
            {
                Source source = new(20);
                ByteSequence bytes = EnumerateBytes(&source);
                foreach (byte value in bytes)
                {
                    if (value != (byte)0) return 20;
                    break;
                }
                if (source.Position != 1 || source.Calls != 1) return 21;
                var empty = bytes.AsEnumerable().Take(0);
                if (empty.Count() != 0 || source.Calls != 1) return 22;
                var query = bytes.AsEnumerable().Where(Even).Take(3);
                if (source.Calls != 1) return 23;
                byte[3] expected = [(byte)2, (byte)4, (byte)6];
                nuint index = 0;
                foreach (byte value in query)
                {
                    if (index >= 3 || value != expected[index]) return 24;
                    index++;
                }
                if (index != 3 || source.Position != 7 || source.Calls != 7) return 25;
                if (ReadByte(&source) != 7 || source.Position != 8) return 26;
                if (bytes.AsEnumerable().Take(2).Count() != 2 || source.Position != 10) return 27;
                if (attempts != 0) return 28;
                return 0;
            }
            """, optimization, queries: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ErrorsInvalidateCurrentAndPropagateWithoutClosingOrReplayingConsumedBytes(int optimization)
    {
        Run(Reader + """
            int main()
            {
                Source source = new(8);
                ByteSequence::Enumerator iterator = EnumerateBytes(&source).GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current != (byte)0) return 30;
                source.FailNext = true;
                bool caught = false;
                try { iterator.MoveNext(); }
                catch (ref Exception error) { caught = error.code == 73; }
                if (!caught || source.Calls != 2 || source.Position != 2 || source.Closed) return 31;
                caught = false;
                try { iterator.Current; }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 2) return 32;
                if (!iterator.MoveNext() || iterator.Current != (byte)2 || source.Calls != 3) return 33;

                source.OverreportNext = true;
                caught = false;
                try { iterator.MoveNext(); }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 4 || source.Position != 3) return 34;
                caught = false;
                try { iterator.Current; }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 4) return 35;
                if (!iterator.MoveNext() || iterator.Current != (byte)3 || source.Calls != 5) return 36;

                source.Closed = true;
                ByteSequence deferred = EnumerateBytes(&source);
                ByteSequence::Enumerator closed = deferred.GetEnumerator();
                if (source.Calls != 5) return 37;
                caught = false;
                try { closed.MoveNext(); }
                catch (ref IOException error) { caught = true; }
                if (!caught || source.Calls != 6 || !source.Closed) return 38;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string program, int optimization, bool queries = false)
    {
        var names = new List<string>
        {
            "core/Exception.gf", "core/ITextWriter.gf", "core/IO/IByteReader.gf",
            "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/IO/byte_reader.gf",
            "core/IO/ByteSequence.gf", "core/IO/byte_reader_enumerate_bytes.gf",
            "core/Collections/IEnumerable.gf", "core/Collections/IEnumerator.gf",
            "libc/Memory/allocation.gf", "libc/Exceptions/unhandled.gf"
        };
        if (queries)
            names.AddRange(["core/IO/byte_sequence_queries.gf", "core/Queries/Enumerable.gf", "core/Queries/SliceSource.gf", "core/Queries/WhereSource.gf",
                "core/Queries/enumerable_slice.gf", "core/Queries/enumerable_where.gf", "core/Queries/enumerable_count.gf"]);
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("byte-sequence-tests.gf", program))
            .Append(new SourceFile("byte-sequence-allocation-hooks.gf", AllocationHooks));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
