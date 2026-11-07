using System.Buffers.Binary;

namespace gflat.Tests.Std;

public sealed class UuidFormatTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("UUID_FORMATS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TextMatchesGuidAndExactParsingPreservesStrictBoundaries(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_uuid_formats_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(187294);
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int sample = 0; sample < 64; sample++)
            {
                byte[] bytes = new byte[16];
                if (sample != 0) random.NextBytes(bytes);
                Guid id = new(bytes, bigEndian: true);
                foreach (char format in "NDBPndbp")
                {
                    string text = id.ToString(format.ToString());
                    Assert.Equal(id, Guid.ParseExact(text, format.ToString()));
                    writer.Write(BinaryPrimitives.ReadUInt64BigEndian(bytes));
                    writer.Write(BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8)));
                    writer.Write((byte)format); writer.Write((uint)text.Length);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes(text));
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Uuid.gf", "core/Text/uuid_string.gf", "core/Text/formattable_string.gf"))
                .Append(new SourceFile("uuid-formats.gf", $$"""
                using Standard;
                using Standard::Text;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    for (int row = 0; row < 512; row++)
                    {
                        ulong[2] halves = default(ulong[2]);
                        char format = '\0'; uint length = 0u;
                        char[39] expected = default(char[39]);
                        if (fread(&halves[0], 8, 2, file!) != 2 || fread(&format, 1, 1, file!) != 1 || fread(&length, 4, 1, file!) != 1) return 2;
                        if (fread(&expected[0], 1, (nuint)length, file!) != (nuint)length) return 3;
                        Uuid id = new(halves[0], halves[1]);
                        for (nuint capacity = 0; capacity <= 39; capacity++)
                        {
                            char[41] output = default(char[41]);
                            for (nuint i = 0; i < 41; i++) output[i] = '!';
                            nuint written = 99;
                            bool success = id.TryFormat(&output[1], capacity, ref written, format);
                            if (success != (capacity >= (nuint)length)) return 4;
                            if (!success)
                            {
                                if (written != 0) return 5;
                                for (nuint i = 0; i < 41; i++) { if (output[i] != '!') return 6; }
                            }
                            else
                            {
                                if (written != (nuint)length || output[0] != '!') return 7;
                                for (nuint i = 0; i < written; i++) { if (output[i + 1] != expected[i]) return 8; }
                                for (nuint i = written + 1; i < 41; i++) { if (output[i] != '!') return 9; }
                            }
                        }
                        Uuid parsed = new(1ul, 2ul);
                        if (!Uuid::TryParseExact(&expected[0], (nuint)length, format, &parsed) || !parsed.Equals(&id)) return 10;
                        Uuid cstring = Uuid::ParseExact(&expected[0], format);
                        if (!cstring.Equals(&id)) return 11;
                        String text = id.ToString(format);
                        if (!text.Equals(&expected[0], (nuint)length)) return 12;
                        String streamed = new();
                        id.Format(&streamed, format);
                        if (!text.Equals(&streamed)) return 13;
                        String standard = id.ToString();
                        if (standard.Length != 36) return 14;
                        for (nuint i = 0; i < (nuint)length; i++)
                        {
                            char original = expected[i]; expected[i] = '?';
                            parsed = new(1ul, 2ul);
                            if (Uuid::TryParseExact(&expected[0], (nuint)length, format, &parsed) || !parsed.IsNil) return 15;
                            expected[i] = original;
                        }
                        for (nuint i = 0; i < (nuint)length; i++)
                        {
                            if (expected[i] >= 'a' && expected[i] <= 'f') expected[i] = (char)(expected[i] - 'a' + 'A');
                        }
                        if (!Uuid::TryParseExact(&expected[0], format, &parsed) || !parsed.Equals(&id)) return 16;
                        parsed = new(1ul, 2ul);
                        if (Uuid::TryParseExact(&expected[0], (nuint)length, 'X', &parsed) || !parsed.IsNil) return 17;
                        bool caught = false;
                        try { parsed = Uuid::ParseExact(&expected[0], (nuint)length, 'X'); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || !parsed.IsNil) return 18;
                        char marker = '!'; nuint written = 99;
                        if (id.TryFormat(&marker, 1, ref written, 'X') || marker != '!' || written != 0) return 19;
                    }
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"UUID formats exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
