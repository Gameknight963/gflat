using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class StringConstructionTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Values
        .Append(new SourceFile("string-construction.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("STRING_CONSTRUCTION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RepeatedBytesAndSuffixRemovalMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_string_construction_" + Guid.NewGuid().ToString("N"));
        try
        {
            var random = new Random(87109);
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int i = 0; i < 4096; i++)
            {
                byte value = (byte)i;
                byte[] prefix = new byte[random.Next(17)];
                random.NextBytes(prefix);
                int count = i % 17 == 0 ? 0 : random.Next(129);
                string initial = Encoding.Latin1.GetString(prefix);
                string expected = new StringBuilder(initial).Append((char)value, count).ToString();
                Assert.Equal(new string((char)value, count), expected[initial.Length..]);
                int cut = random.Next(expected.Length + 1);
                Assert.Equal(expected[..cut], expected.Remove(cut));
                writer.Write((ulong)prefix.Length);
                writer.Write((ulong)count);
                writer.Write((ulong)cut);
                writer.Write((ulong)value);
                writer.Write(prefix);
                writer.Write(Encoding.Latin1.GetBytes(expected));
            }
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard::Text;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* output, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen("{{path.Replace("\\", "\\\\")}}", "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[4] header = default(ulong[4]);
                    char[160] prefix;
                    char[160] expected;
                    while (true)
                    {
                        nuint bytes = fread(&header[0], 1, 32, file!);
                        if (bytes == 0) break;
                        if (bytes != 32) return 2;
                        nuint length = (nuint)header[0];
                        nuint repeatCount = (nuint)header[1];
                        nuint cut = (nuint)header[2];
                        char value = (char)header[3];
                        nuint total = length + repeatCount;
                        if (fread(&prefix[0], 1, length, file!) != length) return 3;
                        if (fread(&expected[0], 1, total, file!) != total) return 4;
                        String text = String::From(&prefix[0], length);
                        text.Append(value, repeatCount);
                        if (!text.Equals(&expected[0], total) || text.Data[text.Length] != '\0') return 5;
                        String repeated = String::From(value, repeatCount);
                        if (!repeated.Equals(&expected[0] + length, repeatCount) || repeated.Data[repeatCount] != '\0') return 6;
                        nuint capacity = text.Capacity;
                        readonly(char)* address = text.Data;
                        if (text.EnsureCapacity(capacity) != capacity || text.Data != address) return 7;
                        text.Remove(cut);
                        if (!text.Equals(&expected[0], cut) || text.Data[cut] != '\0'
                            || text.Capacity != capacity || text.Data != address) return 8;
                    }
                    if (ferror(file!) != 0) return 9;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailuresPreserveStorageAndReservedOperationsDoNotAllocate(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            bool failNext = false;
            bool deny = false;
            bool hugeRequest = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (bytes > 4096) { hugeRequest = true; return null; }
                    if (failNext) { failNext = false; return null; }
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int main()
            {
                {
                    String target = s"old";
                    readonly(char)* address = target.Data;
                    nuint capacity = target.Capacity;
                    int before = live;
                    for (int action = 0; action < 3; action++)
                    {
                        failNext = true;
                        bool caught = false;
                        try
                        {
                            if (action == 0) target.Append('x', 100);
                            if (action == 1) target.EnsureCapacity(100);
                            if (action == 2) target = String::From('x', 100);
                        }
                        catch (BadAllocException* error) { caught = true; }
                        if (!caught || failNext || live != before || target.Data != address
                            || target.Capacity != capacity || !target.Equals("old")) return 1;
                    }
                    nuint maximum = (nuint)0 - (nuint)1;
                    for (int action = 0; action < 4; action++)
                    {
                        bool caught = false;
                        try
                        {
                            if (action == 0) target.Append('x', maximum);
                            if (action == 1) target.EnsureCapacity(maximum);
                            if (action == 2) target = String::From('x', maximum);
                            if (action == 3) target.Remove(maximum);
                        }
                        catch (Exception* error) { caught = true; }
                        if (!caught || hugeRequest || live != before || target.Data != address
                            || target.Capacity != capacity || !target.Equals("old")) return 2;
                    }
                    if (target.EnsureCapacity(64) < 64 || target.Length != 3 || !target.Equals("old")) return 3;
                    address = target.Data;
                    capacity = target.Capacity;
                    int prior = attempts;
                    deny = true;
                    String empty = String::From('\0', 0);
                    empty.Append('x', 0);
                    empty.Remove(0);
                    if (empty.EnsureCapacity(0) != 0 || !empty.IsEmpty) return 4;
                    target.Append('\0', 4);
                    target.Append(target[0], 3);
                    if (target.Length != 10 || target[3] != '\0' || target[6] != '\0'
                        || target[7] != 'o' || target[9] != 'o' || target.Data[10] != '\0') return 5;
                    if (target.EnsureCapacity(8) != capacity) return 6;
                    target.Remove(target.Length);
                    target.Remove(2);
                    deny = false;
                    if (attempts != prior || target.Data != address || target.Capacity != capacity
                        || !target.Equals("ol") || target.Data[2] != '\0') return 7;
                    String growing = new();
                    prior = attempts;
                    for (int i = 0; i < 128; i++) growing.Append('z', 1);
                    if (attempts - prior > 8 || growing.Length != 128 || growing.Data[128] != '\0') return 9;
                    for (nuint i = 0; i < growing.Length; i++) if (growing[i] != 'z') return 10;
                }
                if (live != 0) return 8;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardError}");
    }
}
