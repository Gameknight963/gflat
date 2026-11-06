using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace gflat.Tests.Std.Csv;

public sealed class CsvRecordTests
{
    private const string Imports = """
        using Standard;
        using Standard::Collections;
        using Standard::Csv;
        using Standard::Text;

        """ + "\n";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RecordValuesMatchIndependentParserAndPreserveByteGoldens(int optimization)
    {
        const string document = "one,two,\r\n\"a,b\",\"a\"\"b\"\r\n,,last";
        var rows = new List<byte[][]>();
        using (var oracle = new TextFieldParser(new StringReader(document)))
        {
            oracle.SetDelimiters(",");
            oracle.HasFieldsEnclosedInQuotes = true;
            oracle.TrimWhiteSpace = false;
            while (!oracle.EndOfData) rows.Add(oracle.ReadFields()!.Select(Encoding.UTF8.GetBytes).ToArray());
        }
        var cases = new List<(byte[] Input, byte[][][] Rows)> {
            (Encoding.UTF8.GetBytes(document), rows.ToArray()),
            ([], []),
            ("\n"u8.ToArray(), [[[]]]),
            (","u8.ToArray(), [[[], []]]),
            ("a,\n\n"u8.ToArray(), [["a"u8.ToArray(), []], [[]]]),
            ("\"a\r\nb\nc\rd\",x\r\n"u8.ToArray(), [["a\r\nb\nc\rd"u8.ToArray(), "x"u8.ToArray()]]),
            ([0, 255, 44, 128, 10], [[[0, 255], [128]]]) };
        var globals = new StringBuilder(Imports);
        var body = new StringBuilder("int main() { Vector<String> row = new();\n");
        for (int id = 0; id < cases.Count; id++)
        {
            globals.Append(Bytes("Input" + id, cases[id].Input));
            body.AppendLine($$"""
                {
                    readonly(byte)* bytes = Input{{id}};
                    CsvReader reader = new(bytes.reinterpret<readonly(char)*>(), {{cases[id].Input.Length}});
                """);
            for (int record = 0; record < cases[id].Rows.Length; record++)
            {
                byte[][] expected = cases[id].Rows[record];
                body.AppendLine($$"""
                    if (!reader.TryReadRecord(ref row) || row.Count != {{expected.Length}}) return 1;
                    """);
                for (int field = 0; field < expected.Length; field++)
                {
                    string name = $"Expected{id}_{record}_{field}";
                    globals.Append(Bytes(name, expected[field]));
                    body.AppendLine($$"""
                        if (row[{{field}}].Length != {{expected[field].Length}}) return 2;
                        for (nuint index = 0; index < row[{{field}}].Length; index++)
                            if ((byte)row[{{field}}].Data[index] != {{name}}[index]) return 3;
                        """);
                }
            }
            body.AppendLine("if (reader.TryReadRecord(ref row) || row.Count != 0 || reader.TryReadRecord(ref row)) return 4; }");
        }
        body.AppendLine("return 0; }");
        Run(globals.Append(body).ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReusesFieldStorageRejectsMidrecordCallsAndRetainsCompletedPrefixOnErrors(int optimization)
        => Run(Imports + """
            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* data);
            static class State { public static int Calls; public static int FailAt; public static int Live; public static bool Failing; }
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    State::Calls++;
                    if (State::Failing || State::Calls == State::FailAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) State::Live++;
                    return result;
                }
                public replace void Free(void* data) nothrow { State::Live--; free(data); }
            }
            int main() {
                {
                    Vector<String> row = new(); row.Reserve(4);
                    for (int i = 0; i < 3; i++) { String field = new(); field.Reserve(32); row.Add(move field); }
                    readonly(char)* first = row[0].Data;
                    readonly(char)* second = row[1].Data;
                    CsvReader reuse = new("alpha,beta,gamma\nx,y\n", 21);
                    int calls = State::Calls;
                    State::Failing = true;
                    if (!TryReadRecord(&reuse, ref row) || row.Count != 3) return 1;
                    if (!reuse.TryReadRecord(ref row) || row.Count != 2) return 2;
                    State::Failing = false;
                    if (State::Calls != calls || row[0].Data != first || row[1].Data != second || !row[0].Equals("x") || !row[1].Equals("y")) return 3;
                    CsvReader mixed = new("a,b", 3);
                    mixed.Read();
                    try { mixed.TryReadRecord(ref row); return 4; } catch (ref Exception error) {}
                    if (row.Count != 2 || !row[0].Equals("x") || mixed.FieldOffset != 0) return 5;
                    mixed.Reset();
                    if (!mixed.TryReadRecord(ref row) || row.Count != 2 || !row[0].Equals("a") || !row[1].Equals("b")) return 6;
                    CsvReader invalid = new("ok,b\"ad", 7);
                    try { invalid.TryReadRecord(ref row); return 7; } catch (CsvParseException* error) {}
                    if (row.Count != 1 || !row[0].Equals("ok")) return 8;
                    try { invalid.TryReadRecord(ref row); return 9; } catch (CsvParseException* error) {}
                    if (row.Count != 0) return 10;
                    invalid.Reset();
                    try { invalid.TryReadRecord(ref row); return 20; } catch (CsvParseException* error) {}
                    if (row.Count != 1 || !row[0].Equals("ok")) return 21;
                    CsvReader cr = new("a\rb", 3);
                    try { cr.TryReadRecord(ref row); return 11; } catch (CsvParseException* error) {}
                    if (row.Count != 0) return 12;
                    row.Add(String::From("tiny"));
                    CsvReader growing = new("1234567890123456789012345678901234567890", 40);
                    State::Failing = true;
                    try { growing.TryReadRecord(ref row); return 22; } catch (BadAllocException* error) {}
                    State::Failing = false;
                    if (row.Count != 0) return 23;
                    char[3] input = ['a', ',', 'b'];
                    CsvReader borrowed = new(input, 3);
                    if (!borrowed.TryReadRecord(ref row)) return 13;
                    borrowed.Reset(); input[0] = 'x'; input[2] = 'y';
                    if (!row[0].Equals("a") || !row[1].Equals("b")) return 14;
                }
                if (State::Live != 0) return 15;
                {
                    Vector<String> row = new();
                    CsvReader reader = new("1234567890123456789012345678901234567890", 40);
                    State::FailAt = State::Calls + 2;
                    try { reader.TryReadRecord(ref row); return 16; } catch (BadAllocException* error) {}
                    State::FailAt = 0;
                    if (row.Count != 0 || State::Live != 0) return 17;
                    reader.Reset();
                    if (!reader.TryReadRecord(ref row) || row.Count != 1 || row[0].Length != 40) return 18;
                }
                if (State::Live != 0) return 19;
                return 0;
            }
            """, optimization);

    private static string Bytes(string name, byte[] bytes)
        => $"const byte[{bytes.Length}] {name} = [" + string.Join(",", bytes.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "];\n";

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "core/Csv/CsvReader.gf", "core/Csv/CsvParseException.gf", "core/Csv/CsvReaderText.gf", "core/Csv/csv_reader_records.gf",
            "core/Text/String.gf", "core/Text/string_span.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf",
            "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf",
            "core/Memory/Span.gf", "core/Collections/Vector.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("csv-record-tests.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
