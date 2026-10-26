using System.Globalization;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class JsonReaderMutationTests
{
    private const int MaximumBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MutationsAndTruncationsMatchStrictWholeDocumentAcceptanceWithoutChangingInput(int optimization)
    {
        AssertOracleAnchors();
        List<byte[]> corpus = Corpus();
        Assert.InRange(corpus.Count, 600, 1500);
        bool[] expected = corpus.Select(Accepts).ToArray();
        Assert.Contains(true, expected);
        Assert.Contains(false, expected);

        string directory = Path.Combine(Path.GetTempPath(), "gflat-json-mutations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string manifest = Path.Combine(directory, "corpus.bin");
            using (var writer = new BinaryWriter(File.Create(manifest)))
            {
                writer.Write(corpus.Count);
                for (int row = 0; row < corpus.Count; row++)
                {
                    writer.Write(corpus[row].Length);
                    writer.Write(expected[row]);
                    writer.Write(corpus[row]);
                }
            }

            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("json-mutations.gf", NativeProgram));
            string ir = Path.Combine(directory, "reader.ll");
            string executable = Path.Combine(directory, "reader.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            // One native process consumes the whole corpus, rather than spawning
            // a process for every malformed document.
            var result = NativeToolchain.Run(executable, [manifest], 60000);
            string context = "";
            const string prefix = "Row ";
            if (result.StandardError.StartsWith(prefix, StringComparison.Ordinal))
            {
                string number = new(result.StandardError.AsSpan(prefix.Length).ToString().TakeWhile(char.IsAsciiDigit).ToArray());
                if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int row) && row >= 0 && row < corpus.Count)
                    context = $"\nExpected acceptance {expected[row]}; input hex {Convert.ToHexString(corpus[row])}";
            }
            Assert.True(result.ExitCode == 0, $"JSON mutation batch exit {result.ExitCode}: {result.StandardError}{context}");
            Assert.Empty(result.StandardError);
            Assert.Empty(result.StandardOutput);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void AssertOracleAnchors()
    {
        Assert.True(Accepts("\"\\uD83D\\uDE00\""u8.ToArray()));
        Assert.False(Accepts("\"\\uD800\""u8.ToArray()));
        Assert.False(Accepts("{\"\\uDC00\":0}"u8.ToArray()));
        Assert.True(Accepts("\"\\\\uD800\""u8.ToArray())); // A literal backslash, not a surrogate escape.
        Assert.True(Accepts("{\"a\":1,\"a\":2}"u8.ToArray()));
        Assert.True(Accepts(Encoding.UTF8.GetBytes(new string('9', 180))));
        Assert.True(Accepts("-1e+9999999999"u8.ToArray()));
        Assert.False(Accepts([0xef, 0xbb, 0xbf, 123, 125]));
        Assert.False(Accepts([34, 0xc0, 0x80, 34]));
        Assert.False(Accepts([34, 0xed, 0xa0, 0x80, 34]));
        Assert.False(Accepts([]));
        Assert.False(Accepts("[1,]"u8.ToArray()));
        Assert.False(Accepts("/* comment */0"u8.ToArray()));
    }

    private static bool Accepts(byte[] bytes)
    {
        try
        {
            // JsonDocument may postpone decoding strings. Reject malformed raw
            // UTF-8 first and force every string/property through decoding below.
            _ = StrictUtf8.GetString(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 2048
            });
            DecodeStrings(document.RootElement);
            return true;
        }
        catch (JsonException) { return false; }
        catch (DecoderFallbackException) { return false; }
        catch (EncoderFallbackException) { return false; }
        catch (InvalidOperationException) { return false; } // Invalid escaped surrogate while decoding.
    }

    private static void DecodeStrings(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                _ = StrictUtf8.GetBytes(value.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) DecodeStrings(item);
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    _ = StrictUtf8.GetBytes(property.Name);
                    DecodeStrings(property.Value);
                }
                break;
            // Leave number tokens as lexemes; no numeric conversion/range limit.
        }
    }

    private static List<byte[]> Corpus()
    {
        const int limit = 1400;
        var rows = new List<byte[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(byte[] bytes)
        {
            Assert.InRange(bytes.Length, 0, MaximumBytes);
            if (rows.Count < limit && seen.Add(Convert.ToHexString(bytes))) rows.Add(bytes);
        }
        string[] texts =
        [
            "null", "[true,false,null,0,-1.25e+12]",
            "{\"x\":[0,{\"text\":\"raw \u20ac\U0001f600\"}],\"x\":false}",
            "{\"escape\":\"\\uD83D\\uDE00\\\\\\\"\\n\"}",
            "[{\"a\":[]},[],{}]", "\"\\u0000\\uD7FF\\uE000\"",
            "[-0,1E-9,123456789012345678901234567890]", " \r\n{\"a\":true}\t "
        ];
        var seeds = texts.Select(Encoding.UTF8.GetBytes).ToList();
        var random = new Random(631927);
        for (int row = 0; row < 4; row++)
            seeds.Add(JsonSerializer.SerializeToUtf8Bytes(new { n = random.Next(-99, 100), a = new[] { true, false }, s = row % 2 == 0 ? "x\0y" : "q\\z" }));

        // Put policy anchors and unchanged valid seeds first, before a bounded
        // mutation budget can truncate the generated corpus.
        foreach (byte[] seed in seeds) { Assert.True(Accepts(seed)); Add(seed); }
        Add([]);
        Add(Encoding.UTF8.GetBytes(new string('9', 180)));
        Add("-1e+9999999999"u8.ToArray());
        Add(Encoding.UTF8.GetBytes(new string('[', 16) + "0" + new string(']', 16)));
        Add("\"\\uD800\""u8.ToArray());
        Add("{\"\\uDC00\":0}"u8.ToArray());
        Add("\"\\\\uD800\""u8.ToArray());
        Add([0xef, 0xbb, 0xbf, 123, 125]);
        Add([34, 0xff, 34]);
        Add([34, 0xc0, 0x80, 34]);
        Add([34, 0xed, 0xa0, 0x80, 34]);
        Add([34, 0xf4, 0x90, 0x80, 0x80, 34]);

        foreach (byte[] seed in seeds)
        {
            for (int length = 0; length < seed.Length; length++) Add(seed[..length]);
            for (int at = 0; at < seed.Length; at++) Add(seed[..at].Concat(seed[(at + 1)..]).ToArray());
        }
        byte[] replacements = [0, 9, 10, 32, 34, 44, 58, 91, 93, 123, 125, 92, 0x80, 0xff];
        var positions = seeds.Select(seed => new[] { 0, seed.Length / 2, seed.Length - 1 }
            .Concat(Enumerable.Range(0, seed.Length).Where(index => seed[index] is 34 or 44 or 58 or 91 or 93 or 123 or 125 or 92 || seed[index] >= 128))
            .Distinct().ToArray()).ToArray();
        for (int round = 0; round < positions.Max(value => value.Length); round++)
        for (int row = 0; row < seeds.Count; row++)
        {
            if (round >= positions[row].Length) continue;
            byte[] seed = seeds[row];
            int at = positions[row][round];
            foreach (byte replacement in replacements)
            {
                var changed = seed.ToArray();
                changed[at] = replacement;
                Add(changed);
                Add(seed[..at].Concat(new[] { replacement }).Concat(seed[at..]).ToArray());
            }
        }
        return rows;
    }

    private const string NativeProgram = """
        using std; using std::Text; using std::Json; using std::IO;
        int Report(int row,readonly(char)* message) {
            Console::Error.Write("Row ",4);
            row.Format(Console::Error);
            Console::Error.Write(": ",2);
            Console::Error.WriteLine(message);
            return 1;
        }
        int main(int argc,char** argv) {
            if(argc!=2) return 2;
            int row=-1;
            try {
                File input=File::OpenRead(argv[1]);
                int rows=FileBinary::ReadInt32LittleEndian(&input);
                if(rows<600 || rows>1500) return Report(row,"Invalid manifest row count");
                char[1026] bytes;
                char[1026] saved;
                for(row=0;row<rows;row++) {
                    int size=FileBinary::ReadInt32LittleEndian(&input);
                    int expected=input.ReadByte();
                    if(size<0 || size>1024 || expected<0 || expected>1) return Report(row,"Invalid manifest entry");
                    nuint count=(nuint)size;
                    for(nuint index=0;index<1026;index++) bytes[index]=(char)165;
                    input.ReadExactly(&bytes[1],count);
                    for(nuint index=0;index<1026;index++) saved[index]=bytes[index];
                    bool accepted=true;
                    {
                        JsonReader reader=new(&bytes[1],count);
                        try { while(reader.Read()){} }
                        catch(ref JsonParseException error) {
                            accepted=false;
                            if(error.Offset>count) return Report(row,"JSON error offset exceeds counted input");
                        }
                    }
                    if(accepted!=(expected==1)) return Report(row,"Whole-document acceptance disagrees with strict oracle");
                    for(nuint index=0;index<1026;index++)
                        if(bytes[index]!=saved[index]) return Report(row,"Input or guard byte changed");
                }
                if(input.ReadByte()!=-1) return Report(row,"Trailing manifest data");
                input.Close();
                return 0;
            }
            catch(ref Exception error) {
                Report(row,error.GetMessage());
                return 3;
            }
        }
        """;
}
