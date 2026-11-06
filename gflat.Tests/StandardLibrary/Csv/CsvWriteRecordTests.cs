using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace gflat.Tests.Std.Csv;

public sealed class CsvWriteRecordTests
{
    private const string Prelude = """
        using Standard::Collections;
        using Standard::Csv;
        using Standard::Memory;
        using Standard::Text;

        extern "C" void*? malloc(nuint count);
        extern "C" void free(void* storage);
        int attempts = 0;
        int allocations = 0;
        int releases = 0;
        int destroyed = 0;
        bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                attempts++;
                if (deny) return null;
                void*? result = malloc(count);
                if (result != null) allocations++;
                return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        struct Sink : ITextWriter {
            private char* data;
            private nuint partial;
            public nuint Length;
            public int Calls;
            public int FailAt;
            public Sink(char* output, int failAt = -1, nuint accepted = 0) {
                data = output; partial = accepted; Length = 0; Calls = 0; FailAt = failAt;
            }
            public void Write(readonly(char)* text, nuint count) {
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

    [Fact]
    public void BaseWriterBoundaryPropertyKeepsItsStringIndependentSourceClosure()
    {
        SourceFile[] sources = [Load("core/Csv/CsvWriter.gf"), new("writer-base.gf", """
            using Standard::Csv;

            struct Sink : ITextWriter { public void Write(readonly(char)* data, nuint count) {} }
            int main() {
                Sink sink = new(); CsvWriter writer = new(&sink);
                if (!writer.IsAtRecordBoundary) return 1;
                writer.WriteField("", 0);
                if (writer.IsAtRecordBoundary) return 2;
                writer.WriteEndRecord();
                if (!writer.IsAtRecordBoundary) return 3;
                return 0;
            }
            """)];
        _ = Compiler.Check(sources);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RecordsMatchCanonicalByteOracleAndCompatibleHostParserWithoutCopyingOwners(int optimization)
    {
        byte[][] compatible = ["one"u8.ToArray(), "a,b"u8.ToArray(), "q\"r"u8.ToArray(), "a\r\nb"u8.ToArray(), " leading "u8.ToArray()];
        byte[] compatibleOutput = Encode(compatible);
        using (var parser = new TextFieldParser(new StringReader(Encoding.UTF8.GetString(compatibleOutput))))
        {
            parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true; parser.TrimWhiteSpace = false;
            Assert.Equal(compatible.Select(bytes => Encoding.UTF8.GetString(bytes)), parser.ReadFields());
            Assert.True(parser.EndOfData);
        }
        List<byte[][]> rows = [[], [[]], [[], []], compatible, [[65, 0, 255], [128, 34, 10]]];
        var random = new Random(28811);
        for (int sample = 0; sample < 12; sample++)
            rows.Add(Enumerable.Range(0, random.Next(7)).Select(_ =>
            {
                byte[] bytes = new byte[random.Next(17)]; random.NextBytes(bytes); return bytes;
            }).ToArray());
        byte[] expected = rows.SelectMany(Encode).ToArray();
        Assert.InRange(expected.Length, 1, 4096);
        var code = new StringBuilder(Prelude + "int main() { { char[4099] output; char[32] field; char[32] expectedField;\n");
        code.AppendLine("for (nuint index = 0; index < 4099; index++) output[index] = '?';");
        code.AppendLine("Sink sink = new(&output[1]); CsvWriter writer = new(&sink); Vector<String> row = new();");
        for (int record = 0; record < rows.Count; record++)
        {
            code.AppendLine("{ row.Clear();");
            for (int column = 0; column < rows[record].Length; column++)
            {
                Assign(code, "field", rows[record][column]);
                code.AppendLine($"row.Add(String::From(field, {rows[record][column].Length})); readonly(char)* original{column} = row[{column}].Data;");
            }
            string call = record % 2 == 0 ? "writer.WriteRecord(row.AsImmutable());" : "WriteRecord(&writer, row.AsImmutable());";
            code.AppendLine($$"""
                if (!writer.IsAtRecordBoundary) return 1;
                int before = attempts; deny = true;
                {{call}}
                deny = false;
                if (attempts != before || !writer.IsAtRecordBoundary || destroyed != 0) return 2;
                """);
            for (int column = 0; column < rows[record].Length; column++)
            {
                byte[] field = rows[record][column];
                Assign(code, "expectedField", field);
                code.AppendLine($"if (row[{column}].Data != original{column} || row[{column}].Length != {field.Length}) return 3;");
                code.AppendLine($"for (nuint index = 0; index < {field.Length}; index++) if (row[{column}].Data[index] != expectedField[index]) return 4;");
            }
            code.AppendLine("}");
        }
        code.AppendLine($"if (sink.Length != {expected.Length} || output[0] != '?' || output[{expected.Length + 1}] != '?' || output[4098] != '?') return 5;");
        for (int index = 0; index < expected.Length; index++)
            code.AppendLine($"if (output[{index + 1}] != (char){expected[index]}) return 6;");
        code.AppendLine("} if (destroyed != 1 || allocations != releases) return 7; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ManualPartialRecordsRejectBeforeWritingAndRemainCompletable(int optimization)
        => Run(Prelude + """
            int main() {
                {
                    char[32] output; Sink sink = new(output); CsvWriter writer = new(&sink);
                    Vector<String> row = new(); row.Add(s"two");
                    readonly(char)* original = row[0].Data;
                    writer.WriteField("manual", 6);
                    int calls = sink.Calls; bool caught = false;
                    try { writer.WriteRecord(row.AsImmutable()); } catch (ref Exception error) { caught = true; }
                    if (!caught || sink.Calls != calls || sink.Length != 6 || writer.IsAtRecordBoundary) return 1;
                    writer.WriteEndRecord(); writer.WriteRecord(row.AsImmutable());
                    if (!writer.IsAtRecordBoundary || sink.Length != 13) return 2;
                    writer.WriteField(((nuint)1).reinterpret<readonly(char)*>(), 0);
                    Span<immutable(String)> empty = new(); calls = sink.Calls; caught = false;
                    try { WriteRecord(&writer, empty); } catch (ref Exception error) { caught = true; }
                    if (!caught || sink.Calls != calls || writer.IsAtRecordBoundary) return 3;
                    writer.WriteEndRecord();
                    if (!writer.IsAtRecordBoundary || sink.Length != 15) return 4;
                    if (row.Count != 1 || row[0].Data != original || row[0].Length != 3 || destroyed != 0) return 5;
                }
                if (destroyed != 1 || allocations != releases) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldAndNewlineFailuresPoisonTheWriterWithoutReadingLaterRowsOrTransferringOwners(int optimization)
        => Run(Prelude + """
            int main() {
                {
                    Vector<String> row = new(); row.Add(s"a,b"); row.Add(s"q\"r");
                    readonly(char)* first = row[0].Data; readonly(char)* second = row[1].Data;
                    readonly(char)* expected = "\"a,b\",\"q\"\"r\"\r\n";
                    nuint[10] prefixes = [0, 1, 4, 5, 6, 7, 8, 10, 11, 12];
                    for (int failure = 1; failure <= 10; failure++) for (nuint partial = 0; partial <= 1; partial++) {
                        char[18] output; for (nuint index = 0; index < 18; index++) output[index] = '?';
                        int beforeDestroyed = destroyed;
                        {
                            Sink sink = new(&output[1], failure, partial); CsvWriter writer = new(&sink);
                            bool caught = false;
                            try { writer.WriteRecord(row.AsImmutable()); }
                            catch (ref Exception error) { caught = error.GetCode() == 73; }
                            nuint length = prefixes[(nuint)failure - 1] + partial;
                            if (!caught || sink.Calls != failure || sink.Length != length || writer.IsAtRecordBoundary) return 1;
                            if (output[0] != '?' || output[length + 1] != '?' || output[17] != '?') return 2;
                            for (nuint index = 0; index < length; index++) if (output[index + 1] != expected[index]) return 3;
                            Span<immutable(String)> invalid = new(((nuint)1).reinterpret<immutable(String)*>(), 1);
                            int failures = 0;
                            try { writer.WriteRecord(invalid); } catch (ref Exception error) { failures++; }
                            try { writer.WriteEndRecord(); } catch (ref Exception error) { failures++; }
                            try { writer.WriteField(((nuint)1).reinterpret<readonly(char)*>(), 0); } catch (ref Exception error) { failures++; }
                            if (failures != 3 || sink.Calls != failure || sink.Length != length || destroyed != beforeDestroyed) return 4;
                            if (row.Count != 2 || row[0].Data != first || row[1].Data != second || row[0].Length != 3 || row[1].Length != 3) return 5;
                        }
                        if (destroyed != beforeDestroyed + 1) return 6;
                    }
                }
                if (allocations != releases) return 7;
                return 0;
            }
            """, optimization);

    private static byte[] Encode(byte[][] row)
    {
        var result = new List<byte>();
        for (int index = 0; index < row.Length; index++)
        {
            if (index != 0) result.Add(44);
            byte[] field = row[index];
            bool quote = field.Any(value => value is 44 or 34 or 13 or 10);
            if (quote) result.Add(34);
            foreach (byte value in field) { result.Add(value); if (quote && value == 34) result.Add(34); }
            if (quote) result.Add(34);
        }
        result.Add(13); result.Add(10);
        return result.ToArray();
    }

    private static void Assign(StringBuilder code, string name, byte[] bytes)
    {
        for (int index = 0; index < bytes.Length; index++) code.AppendLine($"{name}[{index}] = (char){bytes[index]};");
    }

    private static SourceFile Load(string name)
        => new(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name)));

    private static void Run(string code, int optimization)
    {
        string[] names = ["core/Csv/CsvWriter.gf", "core/Csv/csv_writer_records.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf",
            "core/Hashing/primitive_hashing.gf", "core/primitive_format.gf", "core/Memory/Span.gf",
            "core/Collections/Vector.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf"];
        var result = CompilerTestHelper.Run(names.Select(Load).Append(new SourceFile("csv-write-record.gf", code)), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
