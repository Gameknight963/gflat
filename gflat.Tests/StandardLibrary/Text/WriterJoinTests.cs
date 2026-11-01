using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class WriterJoinTests
{
    private const string Prelude = """
        using Standard::Collections;
        using Standard::Memory;
        using Standard::Text;

        extern void*? malloc(nuint count);
        extern void free(void* storage);
        int attempts = 0;
        int allocations = 0;
        int releases = 0;
        int destroyed = 0;
        bool deny = false;
        namespace Memory
        {
            public replace void*? Allocate(nuint count)
            {
                attempts++;
                if (deny) return null;
                void*? result = malloc(count);
                if (result != null) allocations++;
                return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        struct Sink : ITextWriter
        {
            private char* data;
            private nuint partial;
            public nuint Length;
            public int Calls;
            public int FailAt;
            public Sink(char* output, int failAt = -1, nuint accepted = 0)
            {
                data = output; partial = accepted; Length = 0; Calls = 0; FailAt = failAt;
            }
            public void Write(readonly(char)* text, nuint count)
            {
                Calls++;
                nuint accepted = count;
                if (Calls == FailAt && partial < accepted) accepted = partial;
                for (nuint index = 0; index < accepted; index++) data[Length + index] = text[index];
                Length += accepted;
                if (Calls == FailAt) throw new Exception("Sink failure", 73);
            }
            ~Sink() { destroyed++; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndCStringJoinsMatchHostOracleAndPreserveOwnerAddressesWithoutAllocating(int optimization)
    {
        var cases = new List<(byte[][] Values, byte[] Separator)>
        {
            ([], []), ([[]], []), ([[], [], []], "::"u8.ToArray()),
            (["one"u8.ToArray()], "ignored"u8.ToArray()),
            ([[], "a"u8.ToArray(), [], []], "|"u8.ToArray()),
            ([[65, 0, 255], [128, 66]], [0, 255, 128]), ([[], []], [])
        };
        var random = new Random(6812);
        for (int sample = 0; sample < 12; sample++)
        {
            byte[][] parts = Enumerable.Range(0, random.Next(7)).Select(_ =>
            {
                byte[] bytes = new byte[random.Next(17)]; random.NextBytes(bytes); return bytes;
            }).ToArray();
            byte[] separator = new byte[random.Next(5)]; random.NextBytes(separator);
            cases.Add((parts, separator));
        }
        var code = new StringBuilder(Prelude + "int main() { char[32] bytes; char[8] separator; char[160] expected; char[163] output;\n");
        foreach (var item in cases)
        {
            code.AppendLine("{ Vector<String> fields = new();");
            for (int index = 0; index < item.Values.Length; index++)
            {
                Assign(code, "bytes", item.Values[index]);
                code.AppendLine($"fields.Add(String::From(bytes, {item.Values[index].Length}));");
                code.AppendLine($"readonly(char)* original{index} = fields[{index}].Data;");
            }
            Assign(code, "separator", item.Separator);
            code.AppendLine($"separator[{item.Separator.Length}] = '\\0';");
            foreach (bool counted in new[] { true, false })
                foreach (bool extension in new[] { false, true })
                {
                    byte[] delimiter = counted ? item.Separator : item.Separator.TakeWhile(value => value != 0).ToArray();
                    var expected = new List<byte>();
                    for (int index = 0; index < item.Values.Length; index++)
                    {
                        if (index != 0) expected.AddRange(delimiter);
                        expected.AddRange(item.Values[index]);
                    }
                    Assign(code, "expected", expected.ToArray());
                    string separatorArguments = counted ? $"separator, {delimiter.Length}" : "separator";
                    string call = extension ? $"writer.WriteJoin(fields.AsImmutable(), {separatorArguments})"
                        : $"WriteJoin(&target, fields.AsImmutable(), {separatorArguments})";
                    code.AppendLine($$"""
                        { for (nuint index = 0; index < 163; index++) output[index] = '?';
                          BufferWriter target = new(&output[1], 161); ITextWriter* writer = &target;
                          int before = attempts; deny = true;
                          {{call}};
                          deny = false;
                          if (attempts != before || target.Length != {{expected.Count}}) return 1;
                          if (output[0] != '?' || output[162] != '?' || output[target.Length + 1] != '\0') return 2;
                          for (nuint index = 0; index < target.Length; index++)
                              if (output[index + 1] != expected[index]) return 3;
                        }
                        """);
                }
            for (int index = 0; index < item.Values.Length; index++)
            {
                Assign(code, "expected", item.Values[index]);
                code.AppendLine($"if (fields[{index}].Data != original{index} || fields[{index}].Length != {item.Values[index].Length}) return 4;");
                code.AppendLine($"for (nuint index = 0; index < {item.Values[index].Length}; index++) if (fields[{index}].Data[index] != expected[index]) return 5;");
            }
            code.AppendLine("}");
        }
        code.AppendLine("if (allocations != releases) return 6; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptySingletonAndZeroSeparatorAvoidUnusedPointerReadsAndEmptyWrites(int optimization)
        => Run(Prelude + """
            int main()
            {
                {
                    char[16] output;
                    Sink sink = new(output);
                    ITextWriter* writer = &sink;
                    Span<immutable(String)> empty = new();
                    readonly(char)* unused = ((nuint)1).reinterpret<readonly(char)*>();
                    deny = true;
                    writer.WriteJoin(empty, unused);
                    WriteJoin(writer, empty, unused, (nuint)0 - 1);
                    deny = false;
                    if (sink.Calls != 0 || sink.Length != 0 || attempts != 0) return 1;
                    Vector<String> fields = new();
                    fields.Add(s"one");
                    int before = attempts; deny = true;
                    WriteJoin(writer, fields.AsImmutable(), unused);
                    writer.WriteJoin(fields.AsImmutable(), unused, (nuint)0 - 1);
                    deny = false;
                    if (attempts != before || sink.Calls != 2 || sink.Length != 6) return 2;
                    if (output[0] != 'o' || output[3] != 'o') return 3;
                    fields.Add(s"two");
                    before = attempts; deny = true;
                    writer.WriteJoin(fields.AsImmutable(), unused, 0);
                    deny = false;
                    if (attempts != before || sink.Calls != 4 || sink.Length != 12) return 4;
                    if (destroyed != 0 || fields.Count != 2 || fields[0].Length != 3 || fields[1].Length != 3) return 5;
                }
                if (destroyed != 1 || allocations != releases) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DestinationFailureLeavesOnlyItsAcceptedPrefixWithoutRetryOrOwnerTransfer(int optimization)
        => Run(Prelude + """
            int main()
            {
                {
                    Vector<String> fields = new();
                    fields.Add(s"AB"); fields.Add(String::From("C\0D", 3)); fields.Add(s"EF");
                    readonly(char)* first = fields[0].Data;
                    readonly(char)* second = fields[1].Data;
                    readonly(char)* third = fields[2].Data;
                    readonly(char)* expected = "AB::C\0D::EF";
                    nuint[5] prefixes = [0, 2, 4, 7, 9];
                    for (int failure = 1; failure <= 5; failure++)
                        for (nuint partial = 0; partial <= 1; partial++)
                        {
                            char[16] output;
                            for (nuint index = 0; index < 16; index++) output[index] = '?';
                            int beforeDestroyed = destroyed;
                            {
                                Sink sink = new(&output[1], failure, partial);
                                bool caught = false;
                                try { WriteJoin(&sink, fields.AsImmutable(), "::", 2); }
                                catch (ref Exception error) { caught = error.GetCode() == 73; }
                                nuint prefix = prefixes[(nuint)failure - 1] + partial;
                                if (!caught || sink.Calls != failure || sink.Length != prefix) return 1;
                                if (output[0] != '?' || output[prefix + 1] != '?' || output[15] != '?') return 2;
                                for (nuint index = 0; index < prefix; index++)
                                    if (output[index + 1] != expected[index]) return 3;
                                if (fields.Count != 3 || fields[0].Data != first || fields[1].Data != second || fields[2].Data != third) return 4;
                                if (fields[0].Length != 2 || fields[1].Length != 3 || fields[2].Length != 2 || fields[1].Data[1] != '\0') return 5;
                                if (destroyed != beforeDestroyed) return 6;
                                sink.FailAt = -1; sink.Write("!", 1);
                                if (sink.Length != prefix + 1 || output[prefix + 1] != '!') return 7;
                            }
                            if (destroyed != beforeDestroyed + 1) return 8;
                        }
                }
                if (allocations != releases) return 9;
                return 0;
            }
            """, optimization);

    private static void Assign(StringBuilder code, string name, byte[] bytes)
    {
        for (int index = 0; index < bytes.Length; index++) code.AppendLine($"{name}[{index}] = (char){bytes[index]};");
    }

    private static void Run(string code, int optimization)
    {
        string[] names = ["core/Text/text_writer_join.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf",
            "core/Text/BufferWriter.gf", "core/Text/BufferTooSmallException.gf", "core/IStringConvertable.gf",
            "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf",
            "core/primitive_format.gf", "core/Memory/Span.gf", "core/Collections/Vector.gf",
            "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("writer-join.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
