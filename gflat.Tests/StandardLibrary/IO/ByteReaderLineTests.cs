using System.Text;

namespace gflat.Tests.Std.IO;

public sealed class ByteReaderLineTests
{
    private const string Source = """
        using Standard::IO; using Standard::Text;
        struct Source : IByteReader {
            private readonly(char)* data; private nuint length; private nuint chunk;
            public nuint Position; public nuint Calls; public nuint ZeroCalls; public nuint Maximum;
            public Source(readonly(char)* input, nuint count, nuint block = 4096) {
                data = input; length = count; chunk = block;
                Position = 0; Calls = 0; ZeroCalls = 0; Maximum = 0;
            }
            public nuint Read(char* output, nuint count) {
                Calls++; if (count == 0) ZeroCalls++; if (count > Maximum) Maximum = count;
                nuint take = length - Position; if (take > count) take = count; if (take > chunk) take = chunk;
                for (nuint i = 0; i < take; i++) output[i] = data[Position + i];
                Position += take; return take;
            }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedWriterLinesMatchLfCrLfAndBareCrAcrossScratchBoundaries(int optimization)
    {
        List<byte[]> inputs = [[], [10], [13, 10], [13], [13, 13, 10], [10, 10],
            "a\r\nb\nlast\r"u8.ToArray(), [0, 255, 13, 65, 10, 128, 0]];
        foreach (int length in new[] { 254, 255, 256, 257, 511, 512, 513 })
        {
            byte[] content = Enumerable.Repeat((byte)65, length).ToArray();
            inputs.Add([.. content, 13, 10]);
            inputs.Add([.. content, 13]);
        }
        var code = new StringBuilder(Source + """
            int allocations = 0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { allocations++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            struct Sink : ITextWriter {
                private char* data; private nuint capacity;
                public nuint Length; public nuint Calls; public nuint ZeroCalls;
                public Sink(char* output, nuint count) { data = output; capacity = count; Length = 1; Calls = 0; ZeroCalls = 0; data[0] = '!'; }
                public void Write(readonly(char)* input, nuint count) {
                    Calls++; if (count == 0) ZeroCalls++;
                    if (count > capacity - Length) throw new IOException("Test sink overflow");
                    for (nuint i = 0; i < count; i++) data[Length + i] = input[i];
                    Length += count;
                }
            }
            int main() {
            """);
        foreach (byte[] input in inputs)
        {
            string bytes = input.Length == 0 ? "(char)0" : string.Join(",", input.Select(value => "(char)" + value));
            code.AppendLine($"{{char[{Math.Max(1, input.Length)}] input=[{bytes}];Source source=new(input,{input.Length});char[620] output;");
            foreach (var line in Lines(input))
            {
                code.AppendLine("{for(nuint i=0;i<620;i++)output[i]='?';Sink sink=new(&output[1],618);");
                code.AppendLine($"if(!TryReadLine(&source,&sink)||sink.Length!={line.Bytes.Length + 1}||source.Position!={line.Consumed})return 1;");
                code.AppendLine($"if(sink.ZeroCalls!=0||output[0]!='?'||output[1]!='!'||output[{line.Bytes.Length + 2}]!='?')return 2;");
                for (int i = 0; i < line.Bytes.Length; i++) code.AppendLine($"if(output[{i + 2}]!=(char){line.Bytes[i]})return 3;");
                code.AppendLine("}");
            }
            code.AppendLine($$"""
                Sink eof=new(output,620);if(TryReadLine(&source,&eof)||eof.Length!=1||eof.Calls!=0)return 4;
                if(source.Position!={{input.Length}}||source.ZeroCalls!=0||source.Maximum>1)return 5;}
                """);
        }
        code.AppendLine("return allocations;}");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StringReuseClearsBeforeEofAndReturningLinesOwnTheirContents(int optimization)
        => Run(Source + """
            int main() {
                Source source = new("first\n\nlast", 11);
                String line = s"old"; line.Reserve(512);
                readonly(char)* storage = line.Data; nuint capacity = line.Capacity;
                if (!TryReadLine(&source, ref line) || !line.Equals("first") || line.Data != storage || line.Capacity != capacity) return 10;
                if (!TryReadLine(&source, ref line) || line.Length != 0 || line.Data != storage) return 11;
                if (!TryReadLine(&source, ref line) || !line.Equals("last") || line.Data != storage) return 12;
                if (TryReadLine(&source, ref line) || line.Length != 0 || line.Data != storage || line.Data[0] != '\0') return 13;
                char[5] counted = ['a', '\0', (char)255, 'b', '\n'];
                Source binary = new(counted, 5);
                if (!TryReadLine(&binary, ref line) || line.Length != 4 || line.Data[1] != '\0' || line.Data[2] != (char)255 || line.Data[3] != 'b') return 14;
                Source owned = new("one\ntwo\n", 8);
                String first = ReadLine(&owned); String second = ReadLine(&owned);
                second[0] = 'T';
                if (!first.Equals("one") || !second.Equals("Two")) return 15;
                bool ended = false;
                try { String missing = ReadLine(&owned); }
                catch (ref EndOfStreamException error) { ended = true; }
                if (!ended) return 16;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OneRetainedBufferedReaderPreservesCrLfBoundariesAndFollowingBytes(int optimization)
        => Run(Source + """
            int main() {
                nuint[5] capacities = [1, 2, 3, 4, 16]; nuint[3] chunks = [1, 2, 9];
                for (nuint c = 0; c < 5; c++) for (nuint b = 0; b < 3; b++) {
                    Source source = new("A\r\nB\nTAIL", 9, chunks[b]);
                    BufferedReader input = new(&source, capacities[c]); String line = new(); line.Reserve(8);
                    if (!input.TryReadLine(ref line) || !line.Equals("A")) return 20;
                    if (!input.TryReadLine(ref line) || !line.Equals("B")) return 21;
                    char[4] tail; ReadExactly(&input, tail, 4);
                    if (tail[0] != 'T' || tail[1] != 'A' || tail[2] != 'I' || tail[3] != 'L') return 22;
                    if (source.Position != 9 || source.ZeroCalls != 0) return 23;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InputFailuresPreserveOnlyCommittedStringChunksAndPropagateTheOriginalException(int optimization)
        => Run("""
            using Standard::IO; using Standard::Text;
            class InputFailure : IOException { public InputFailure() : base("input failure") { code = 81; } }
            struct Source : IByteReader {
                public nuint Position; private nuint failAt;
                public Source(nuint position) { Position = 0; failAt = position; }
                public nuint Read(char* data, nuint count) {
                    if (Position == failAt) { data[0] = 'Z'; Position++; throw new InputFailure(); }
                    data[0] = 'a'; Position++; return 1;
                }
            }
            int main() {
                nuint[2] failures = [0, 270];
                for (nuint i = 0; i < 2; i++) {
                    Source source = new(failures[i]); String line = s"old"; line.Reserve(512);
                    bool caught = false;
                    try { TryReadLine(&source, ref line); }
                    catch (ref InputFailure error) { caught = true; if (error.GetCode() != 81) return 30; }
                    if (!caught || source.Position != failures[i] + 1) return 31;
                    if (line.Length > failures[i] || line.Data[line.Length] != '\0') return 32;
                    for (nuint n = 0; n < line.Length; n++) if (line.Data[n] != 'a') return 33;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WriterFailuresStopWithoutDrainingAndOverReportsAreCheckedBeforeTheByte(int optimization)
        => Run("""
            using Standard::IO; using Standard::Text;
            class OutputFailure : IOException { public OutputFailure() : base("output failure") { code = 82; } }
            struct Source : IByteReader {
                public nuint Position; public nuint Length; public int Mode;
                public Source(int mode) { Position = 0; Length = 600; Mode = mode; }
                public nuint Read(char* data, nuint count) {
                    if (Position == Length) return 0;
                    Position++;
                    if (Mode == 1) { data[0] = '\n'; return 2; }
                    if (Mode == 2) { data[0] = '\n'; return (nuint)0 - (nuint)1; }
                    data[0] = 'a'; return 1;
                }
            }
            struct Sink : ITextWriter {
                private Source* source;
                public int Calls; public nuint Written; public nuint ReadPosition; public nuint Offered;
                public Sink(Source* input) { source = input; Calls = 0; Written = 0; ReadPosition = 0; Offered = 0; }
                public void Write(readonly(char)* data, nuint count) {
                    Calls++; ReadPosition = source.Position; Offered = count;
                    nuint take = count; if (take > 3) take = 3;
                    Written += take; throw new OutputFailure();
                }
            }
            int main() {
                for (int mode = 0; mode < 3; mode++) {
                    Source source = new(mode); Sink sink = new(&source); bool caught = false;
                    try { TryReadLine(&source, &sink); }
                    catch (ref IOException error) {
                        caught = true; if (mode == 0 && error.GetCode() != 82) return 40;
                    }
                    if (!caught) return 41;
                    if (mode == 0 && (source.Position != sink.ReadPosition || sink.Calls != 1 || sink.Written == 0 || sink.Written > sink.Offered)) return 42;
                    if (mode != 0 && (source.Position != 1 || sink.Calls != 0)) return 43;
                }
                Source longLine = new(0); char[16] data; BufferWriter output = new(data, 16); bool small = false;
                try { TryReadLine(&longLine, &output); }
                catch (ref BufferTooSmallException error) { small = true; }
                if (!small || output.Length > output.Capacity || longLine.Position <= output.Length || longLine.Position > longLine.Length || data[output.Length] != '\0') return 44;
                for (nuint i = 0; i < output.Length; i++) if (data[i] != 'a') return 45;
                return 0;
            }
            """, optimization);

    [Fact]
    public void WriterHelperChecksWithoutStringOrLibcWhenHostExceptionMemoryIsProvided()
    {
        string[] files = ["core/Exception.gf", "core/ITextWriter.gf", "core/IO/IOException.gf", "core/IO/IByteReader.gf", "core/IO/byte_reader_lines.gf"];
        var sources = files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", file))))
            .Append(new SourceFile("minimal-byte-lines.gf", """
                using Standard::IO;
                namespace Memory {
                    private extern void*? HostAllocate(nuint count); private extern void HostFree(void* storage);
                    public void*? Allocate(nuint count) => HostAllocate(count);
                    public void Free(void* storage) nothrow { HostFree(storage); }
                }
                struct Input : IByteReader { public nuint Read(char* data, nuint count) => 0; }
                struct Output : ITextWriter { public void Write(readonly(char)* data, nuint count) {} }
                int main() { Input input = new(); Output output = new(); if (TryReadLine(&input, &output)) return 1; return 0; }
                """));
        Compiler.Check(sources, options: new() { NoStdLib = true, NoBuiltins = true });
    }

    private static List<(byte[] Bytes, int Consumed)> Lines(byte[] input)
    {
        var result = new List<(byte[], int)>(); int start = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != 10) continue;
            int end = i; if (end > start && input[end - 1] == 13) end--;
            result.Add((input[start..end], i + 1)); start = i + 1;
        }
        if (start < input.Length) result.Add((input[start..], input.Length));
        return result;
    }

    private static void Run(string code, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(file => new SourceFile(file, File.ReadAllText(file))).Append(new SourceFile("byte-reader-lines.gf", code));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
