using System.Text;

namespace gflat.Tests;

public sealed class StandardAsciiHashingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedHashesMatchAnUnsignedHostOracleAndPreserveEveryInputByte(int optimization)
    {
        List<byte[]> samples = [[], [0], [65, 0, 98], [97, 0, 66], [192, 224, 255]];
        samples.AddRange(Enumerable.Range(0, 256).Select(value => new[] { (byte)value }));
        samples.Add(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        var random = new Random(391857);
        for (int sample = 0; sample < 48; sample++)
        {
            byte[] bytes = new byte[random.Next(257)];
            random.NextBytes(bytes);
            samples.Add(bytes);
        }

        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, leaveOpen: true))
        {
            foreach (byte[] bytes in samples)
            {
                writer.Write((ushort)bytes.Length);
                writer.Write(bytes);
                writer.Write(Oracle(bytes));
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        string program = $$$"""
            using std; using std::Collections;
            int attempts = 0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { attempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            int Digit(char value) nothrow {
                if (value <= '9') return value - '0';
                return value - 'A' + 10;
            }
            int ReadByte(readonly(char)* packet, ref nuint position) nothrow {
                int value = (Digit(packet[position]) << 4) | Digit(packet[position + 1]);
                position += 2;
                return value;
            }
            uint ReadUInt(readonly(char)* packet, ref nuint position) nothrow {
                uint value = 0u;
                for (int shift = 0; shift < 32; shift += 8)
                    value = value | ((uint)ReadByte(packet, ref position) << shift);
                return value;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                for (int sample = 0; sample < {{{samples.Count}}}; sample++) {
                    nuint count = (nuint)ReadByte(packet, ref position);
                    count += (nuint)ReadByte(packet, ref position) * 256;
                    char[258] storage;
                    char[258] alternate;
                    char[256] saved;
                    char[256] alternateSaved;
                    for (nuint i = 0; i < 258; i++) {
                        storage[i] = (char)165;
                        alternate[i] = (char)165;
                    }
                    for (nuint i = 0; i < count; i++) {
                        char value = (char)ReadByte(packet, ref position);
                        storage[i + 1] = value;
                        saved[i] = value;
                        byte unsignedValue = (byte)value;
                        if (unsignedValue >= (byte)65 && unsignedValue <= (byte)90)
                            value = (char)((uint)unsignedValue + 32u);
                        else if (unsignedValue >= (byte)97 && unsignedValue <= (byte)122)
                            value = (char)((uint)unsignedValue - 32u);
                        alternate[i + 1] = value;
                        alternateSaved[i] = value;
                    }
                    nuint expected = (nuint)ReadUInt(packet, ref position);
                    if (Ascii::HashIgnoreCase(&storage[1], count) != expected) return 1;
                    if (Ascii::HashIgnoreCase(&alternate[1], count) != expected) return 2;
                    if (!Ascii::EqualsIgnoreCase(&storage[1], count, &alternate[1], count)) return 3;
                    for (nuint i = 0; i < count; i++)
                        if (storage[i + 1] != saved[i] || alternate[i + 1] != alternateSaved[i]) return 4;
                    if ((byte)storage[0] != (byte)165 || (byte)alternate[0] != (byte)165) return 5;
                    for (nuint i = count + 1; i < 258; i++)
                        if ((byte)storage[i] != (byte)165 || (byte)alternate[i] != (byte)165) return 6;
                }
                readonly(char)*? absent = null;
                if (Ascii::HashIgnoreCase(absent!, 0) != (nuint){{{Oracle([])}}}u) return 7;
                if (position != {{{hex.Length}}} || attempts != 0) return 8;
                return 0;
            }
            """;
        Run(program, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MatchingBorrowedKeyEqualityFindsOriginalSpellingWithoutExtraAllocation(int optimization)
    {
        Run("""
            using std; using std::Collections;
            extern void*? malloc(nuint count);
            extern void free(void* storage);
            int attempts = 0;
            int allocations = 0;
            int releases = 0;
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
            struct Key : IHashable, IEquatable<Key> {
                public readonly(char)* Text;
                public nuint Length;
                public Key(readonly(char)* text, nuint length) { Text = text; Length = length; }
                public immutable nuint GetHashCode() nothrow => Ascii::HashIgnoreCase(Text, Length);
                public immutable bool Equals(immutable(Key)* other) nothrow
                    => Ascii::EqualsIgnoreCase(Text, Length, other.Text, other.Length);
            }
            int main() {
                {
                    HashMap<Key, int> values = new();
                    values.Reserve(16);
                    values.Add(new Key("Content-Type", 12), 7);
                    values.Add(new Key("A\0b", 3), 9);
                    int before = attempts;
                    deny = true;
                    Key query = new("content-type", 12);
                    int*? found = values.Find(&query);
                    if (found == null) return 1;
                    if (*found! != 7 || values.TryAdd(new Key("CONTENT-TYPE", 12), 99)) return 2;
                    values.Set(new Key("CoNtEnT-tYpE", 12), 11);
                    found = values.Find(&query);
                    if (found == null) return 3;
                    if (*found! != 11 || values.Count != 2) return 4;
                    Key counted = new("a\0B", 3);
                    found = values.Find(&counted);
                    if (found == null) return 5;
                    if (*found! != 9) return 6;
                    var iterator = values.GetEnumerator();
                    bool original = false;
                    while (iterator.MoveNext()) {
                        immutable(Key)* key = iterator.Current.Key;
                        if (key.Length == 12) {
                            original = CString::Equals(key.Text, "Content-Type");
                        }
                    }
                    if (!original || attempts != before) return 7;
                    deny = false;
                }
                if (allocations != releases) return 8;
                return 0;
            }
            """, optimization);
    }

    [Fact]
    public void HashingNeedsOnlyTheAsciiSourceAndNoRuntimeBoundary()
    {
        SourceFile[] sources = [
            new("Ascii.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Ascii.gf"))),
            new("ascii-hash-only.gf", "nuint Observe(readonly(char)* data,nuint count)nothrow=>std::Ascii::HashIgnoreCase(data,count);int main()=>0;")
        ];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        Assert.DoesNotContain("__gflat_eh_", ir);
        Assert.DoesNotContain("@malloc", ir);
        Assert.DoesNotContain("$String", ir);
        Assert.DoesNotContain("Hashing::", ir);
    }

    private static uint Oracle(ReadOnlySpan<byte> bytes)
    {
        uint hash = 2166136261;
        foreach (byte value in bytes)
        {
            uint folded = value is >= 97 and <= 122 ? (uint)value - 32 : value;
            hash = unchecked((hash ^ folded) * 16777619);
        }
        return hash;
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("ascii-hashing.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"ASCII hashing exit {result.ExitCode}: {result.StandardError}");
    }
}
