namespace gflat.Tests;

public sealed class StandardStringTextComparisonTests
{
    // CString.gf is intentionally absent: the String methods scan locally.
    private static IEnumerable<SourceFile> Sources(string program)
        => new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Collections/Comparer.gf",
            "Hashing.gf", "IStringConvertable.gf", "String.gf", "ByteSearch.gf" }
            .Select(name => new SourceFile(name,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("string-text-comparison.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndCStringOrderingMatchIndependentUnsignedByteOracle(int optimization)
    {
        var cases = new List<(byte[] Owned, byte[] Input)>
        {
            ([], []), ([], [0]), ([], [1]), ([0], []), ([0], [0]),
            ([97, 0, 98], [97, 0, 98]), ([97, 0, 98], [97]),
            ([97], [97, 0, 98]), ([0, 255], [1]), ([255], [128]),
            ([128], [127]), ([1], [0, 255]), ([97, 0], [97, 0])
        };
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            byte current = (byte)value;
            cases.Add(([current], [current]));
            cases.Add(([current], [(byte)(255 - value)]));
            cases.Add(([current], [0, current]));
        }
        var random = new Random(407132);
        foreach (int length in new[] { 0, 1, 2, 31, 32, 63, 64, 65, 255, 256, 257 })
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            AddVariants(bytes);
            if (length != 0)
            {
                bytes[length / 2] = 0;
                AddVariants(bytes);
            }
        }
        AddVariants(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        AddVariants(Enumerable.Range(0, 256).Reverse().Select(value => (byte)value).ToArray());
        for (int sample = 0; sample < 24; sample++)
        {
            byte[] left = new byte[random.Next(0, 66)];
            byte[] right = new byte[random.Next(0, 66)];
            random.NextBytes(left);
            random.NextBytes(right);
            cases.Add((left, right));
        }

        void AddVariants(byte[] bytes)
        {
            cases.Add(((byte[])bytes.Clone(), (byte[])bytes.Clone()));
            cases.Add(((byte[])bytes.Clone(), [.. bytes, 1]));
            cases.Add(((byte[])bytes.Clone(), bytes.Length == 0 ? [1] : bytes[..^1]));
            if (bytes.Length == 0) return;
            byte[] changed = (byte[])bytes.Clone();
            changed[bytes.Length / 2] ^= 255;
            cases.Add(((byte[])bytes.Clone(), changed));
        }

        static int Compare(byte[] left, byte[] right)
            => Math.Sign(left.AsSpan().SequenceCompareTo(right));
        static byte[] CString(byte[] bytes)
        {
            int zero = Array.IndexOf(bytes, (byte)0);
            return zero < 0 ? bytes : bytes[..zero];
        }
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (var (owned, input) in cases)
            {
                Assert.InRange(owned.Length, 0, 258);
                Assert.InRange(input.Length, 0, 258);
                writer.Write((ushort)owned.Length);
                writer.Write((ushort)input.Length);
                writer.Write((byte)(Compare(owned, input) + 1));
                writer.Write((byte)(Compare(owned, CString(input)) + 1));
                writer.Write(owned);
                writer.Write(input);
            }
        }
        string hex = Convert.ToHexString(packet.ToArray()).ToLowerInvariant();

        Run($$$"""
            using std;
            int Sign(int value) nothrow {
                if (value < 0) return -1;
                if (value > 0) return 1;
                return 0;
            }
            int Nibble(char value) nothrow {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'a' + 10;
            }
            int Next(readonly(char)* packet, ref nuint position) nothrow {
                int value = Nibble(packet[position]) * 16 + Nibble(packet[position + 1]);
                position += 2;
                return value;
            }
            nuint Length(readonly(char)* packet, ref nuint position) nothrow {
                int low = Next(packet, ref position);
                int high = Next(packet, ref position);
                return (nuint)(low + high * 256);
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                char[262] owners;
                char[262] inputs;
                char[262] savedOwners;
                char[262] savedInputs;
                for (nuint i = 0; i < {{{cases.Count}}}; i++) {
                    nuint ownerLength = Length(packet, ref position);
                    nuint inputLength = Length(packet, ref position);
                    int counted = Next(packet, ref position) - 1;
                    int terminated = Next(packet, ref position) - 1;
                    if (ownerLength > 258 || inputLength > 258) return 10;
                    for (nuint j = 0; j < 262; j++) { owners[j] = (char)165; inputs[j] = (char)165; }
                    for (nuint j = 0; j < ownerLength; j++) owners[j + 1] = (char)Next(packet, ref position);
                    for (nuint j = 0; j < inputLength; j++) inputs[j + 1] = (char)Next(packet, ref position);
                    owners[ownerLength + 1] = '\0';
                    inputs[inputLength + 1] = '\0';
                    for (nuint j = 0; j < 262; j++) { savedOwners[j] = owners[j]; savedInputs[j] = inputs[j]; }
                    readonly(char)* source = &owners[1];
                    readonly(char)* text = &inputs[1];
                    String owned = String::From(source, ownerLength);
                    String other = String::From(text, inputLength);
                    immutable(String)* view = &owned;
                    readonly(char)* storage = owned.Data;
                    nuint capacity = owned.Capacity;
                    if (Sign(owned.CompareTo(text, inputLength)) != counted) return 1;
                    if (Sign(view.CompareTo(text, inputLength)) != counted) return 2;
                    if (Sign(owned.CompareTo(text)) != terminated) return 3;
                    if (Sign(view.CompareTo(text)) != terminated) return 4;
                    if (Sign(view.CompareTo(&other)) != counted) return 5;
                    if (owned.Data != storage || owned.Length != ownerLength || owned.Capacity != capacity) return 6;
                    if (owned.Data[owned.Length] != '\0') return 7;
                    for (nuint j = 0; j < owned.Length; j++)
                        if (owned.Data[j] != source[j]) return 8;
                    for (nuint j = 0; j < 262; j++)
                        if (owners[j] != savedOwners[j] || inputs[j] != savedInputs[j]) return 9;
                }
                if (position != {{{hex.Length}}}) return 11;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AliasesQualifiedReceiversAndExistingComparisonContractsAllocateNothing(int optimization)
        => Run("""
            using std;
            int attempts = 0;
            int live = 0;
            bool deny = false;
            extern void*? malloc(nuint bytes);
            extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int Contract<T : IComparable<T>>(immutable(T)* left, immutable(T)* right)
                => left.CompareTo(right);
            int Borrow(ref immutable(String) value, readonly(char)* text) nothrow
                => value.CompareTo(text);
            int main() {
                {
                    String empty = new();
                    String value = s"a\0b";
                    String same = s"a\0b";
                    String prefix = s"a";
                    String original = s"abcd";
                    String suffix = s"bcd";
                    char[1] highBytes = [(char)255];
                    String high = String::From(highBytes, 1);
                    String ascii = s"z";
                    value.Reserve(40);
                    immutable(String)* view = &value;
                    readonly(String)* readView = &value;
                    IComparable<String>* comparison = &value;
                    readonly(char)* storage = value.Data;
                    nuint capacity = value.Capacity;
                    readonly(char)*? absent = null;
                    int baseline = attempts;
                    deny = true;
                    if (empty.CompareTo(absent!, 0) != 0 || value.CompareTo(absent!, 0) <= 0) return 20;
                    if (value.CompareTo(value.Data, value.Length) != 0 || value.CompareTo(value.Data) <= 0) return 21;
                    if (suffix.CompareTo(original.Data + 1, 3) != 0 || suffix.CompareTo(original.Data + 1) != 0) return 22;
                    if (view.CompareTo("a") <= 0 || readView.CompareTo("a\0ignored") <= 0) return 23;
                    if (prefix.CompareTo(value.Data) != 0 || prefix.CompareTo(value.Data, 3) >= 0) return 24;
                    if (empty.CompareTo("") != 0 || empty.CompareTo("x") >= 0) return 25;
                    if (Borrow(ref value, "a") <= 0 || view.CompareTo(&same) != 0) return 26;
                    if (comparison.CompareTo(&same) != 0 || Contract<String>(&value, &prefix) <= 0) return 27;
                    if (Comparer<String>::Compare(&high, &ascii) <= 0 || Comparer<String>::Compare(&prefix, &value) >= 0) return 28;
                    if (attempts != baseline || value.Data != storage || value.Capacity != capacity) return 29;
                    if (value.Length != 3 || value.Data[0] != 'a' || value.Data[1] != '\0' || value.Data[2] != 'b' || value.Data[3] != '\0') return 30;
                    deny = false;
                }
                if (live != 0) return 31;
                return 0;
            }
            """, optimization);
}
