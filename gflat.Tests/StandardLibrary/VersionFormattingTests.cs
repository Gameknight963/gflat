namespace gflat.Tests.Std;

public sealed class VersionFormattingTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VERSION_FORMATTING.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldCountsAndRevisionHalvesMatchDotNetWithGuardedBuffers(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_version_fields_" + Guid.NewGuid().ToString("N"));
        try
        {
            var versions = new List<Version> { new(), new(1, 2), new(1, 2, 0), new(1, 2, 3, 4) };
            foreach (int revision in new[] { 0, 1, 32767, 32768, 65535, 65536, 2147483647 })
                versions.Add(new Version(int.MaxValue, int.MaxValue, int.MaxValue, revision));
            var random = new Random(827194);
            for (int i = 0; i < 64; i++) versions.Add(new(random.Next(), random.Next(), random.Next(), random.Next()));
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (Version version in versions)
            foreach (int fields in new[] { int.MinValue, -1, 0, 1, 2, 3, 4, 5, int.MaxValue })
            {
                bool valid = true;
                string text = "";
                try { text = version.ToString(fields); }
                catch (ArgumentException) { valid = false; }
                foreach (int item in new[] { version.Major, version.Minor, version.Build, version.Revision,
                    fields, valid ? 1 : 0, text.Length, version.MajorRevision, version.MinorRevision }) writer.Write(item);
                writer.Write(System.Text.Encoding.ASCII.GetBytes(text));
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Version.gf", "core/Text/version_string.gf", "core/Text/formattable_string.gf"))
                .Append(new SourceFile("version-fields.gf", $$"""
                using Standard;
                using Standard::Text;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Render(immutable(Version)* value, char* output, nuint capacity, ref nuint written, int fields) nothrow
                    => value.TryFormat(output, capacity, ref written, fields);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[9] row = default(int[9]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 36, file!);
                        if (received == 0) break;
                        if (received != 36) return 2;
                        char[43] expected = default(char[43]);
                        if (fread(&expected[0], 1, (nuint)row[6], file!) != (nuint)row[6]) return 3;
                        Version version = new(row[0], row[1], row[2], row[3]);
                        if ((int)version.MajorRevision != row[7] || (int)version.MinorRevision != row[8]) return 4;
                        for (nuint capacity = 0; capacity <= 44; capacity++)
                        {
                            char[46] output = default(char[46]);
                            for (nuint i = 0; i < 46; i++) output[i] = '!';
                            nuint written = 99;
                            bool success = Render(&version, &output[1], capacity, ref written, row[4]);
                            bool expectedSuccess = row[5] == 1 && capacity >= (nuint)row[6];
                            if (success != expectedSuccess) return 5;
                            if (!success)
                            {
                                if (written != 0) return 6;
                                for (nuint i = 0; i < 46; i++) { if (output[i] != '!') return 7; }
                            }
                            else
                            {
                                if (written != (nuint)row[6] || output[0] != '!') return 8;
                                for (nuint i = 0; i < written; i++) { if (output[i + 1] != expected[i]) return 9; }
                                for (nuint i = written + 1; i < 46; i++) { if (output[i] != '!') return 10; }
                            }
                        }
                        String text = s"unchanged";
                        bool caught = false;
                        try { text = version.ToString(row[4]); }
                        catch (Exception* error) { caught = true; }
                        if (row[5] == 1)
                        {
                            if (caught || !text.Equals(&expected[0], (nuint)row[6])) return 11;
                            String writer = new();
                            version.Format(&writer, row[4]);
                            if (!writer.Equals(&text)) return 12;
                        }
                        else
                        {
                            if (!caught || !text.Equals("unchanged")) return 13;
                            caught = false;
                            try { version.Format(&text, row[4]); }
                            catch (Exception* error) { caught = true; }
                            if (!caught || !text.Equals("unchanged")) return 14;
                        }
                        String full = version.ToString();
                        nuint fullLength = 0;
                        version.TryFormat(&expected[0], 43, ref fullLength);
                        if (!full.Equals(&expected[0], fullLength)) return 15;
                    }
                    if (ferror(file!) != 0) return 16;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Version fields exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
