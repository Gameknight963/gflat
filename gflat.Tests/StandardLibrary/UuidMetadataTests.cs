using System.Buffers.Binary;

namespace gflat.Tests.Std;

public sealed class UuidMetadataTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("UUID_METADATA.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FieldsMatchDotNetAcrossEveryNibbleAndRandomNetworkBytePatterns(int optimization)
    {
        var random = new Random(821764);
        string path = Path.Combine(Path.GetTempPath(), "gflat_uuid_metadata_" + Guid.NewGuid().ToString("N"));
        try
        {
            const int cases = 512;
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int i = 0; i < cases; i++)
            {
                byte[] bytes = new byte[16]; random.NextBytes(bytes);
                if (i < 256)
                {
                    bytes[6] = (byte)((i & 15) << 4 | bytes[6] & 15);
                    bytes[8] = (byte)((i >> 4) << 4 | bytes[8] & 15);
                }
                var guid = new Guid(bytes, bigEndian: true);
                writer.Write(BinaryPrimitives.ReadUInt64BigEndian(bytes));
                writer.Write(BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8)));
                writer.Write(guid.Version); writer.Write(guid.Variant);
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var sources = StandardTestSources.Values
                .Concat(StandardTestSources.Load("core/Uuid.gf"))
                .Append(new SourceFile("uuid-metadata.gf", $$"""
                    using Standard;

                    extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                    extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                    extern "C" int fclose(void* file);
                    int main()
                    {
                        void*? file = fopen({{literal}}, "rb");
                        if (file == null) return 1;
                        defer { fclose(file!); }
                        Uuid nil = new();
                        if (nil.Version != 0 || nil.Variant != 0) return 2;
                        for (int i = 0; i < {{cases}}; i++)
                        {
                            ulong high = 0ul;
                            ulong low = 0ul;
                            int version = 0;
                            int variant = 0;
                            if (fread(&high, 8, 1, file!) != 1 || fread(&low, 8, 1, file!) != 1 ||
                                fread(&version, 4, 1, file!) != 1 || fread(&variant, 4, 1, file!) != 1) return 3;
                            immutable(Uuid) value = new(high, low);
                            if (value.Version != version || value.Variant != variant) return 4;
                            byte[16] bytes;
                            nuint written = 0;
                            if (!value.TryWriteBytes(bytes, 16, ref written)) return 5;
                            Uuid restored = Uuid::FromBytes(bytes, 16);
                            if (restored.Version != version || restored.Variant != variant || !restored.Equals(&value)) return 6;
                            if (value.High != high || value.Low != low) return 7;
                        }
                        return 0;
                    }
                    """));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
