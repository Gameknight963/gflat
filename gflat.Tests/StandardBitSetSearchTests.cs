using System.Collections;
using System.Text;

namespace gflat.Tests;

public sealed class StandardBitSetSearchTests
{
    private const string Allocator = """
        using std;
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
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallLengthSingleBitAndSingleHoleMatchAnalyticSearchesWithoutAllocation(int optimization)
    {
        Run(Allocator + """
            bool Check(immutable(BitSet)* bits, int mode, nuint special) {
                nuint length = bits.Length;
                nuint hash = bits.GetHashCode();
                for (nuint start = 0; start <= length; start++) {
                    nuint set = length;
                    nuint clear = length;
                    if (mode == 0) clear = start;
                    else if (mode == 1) set = start;
                    else if (mode == 2) {
                        if (start <= special) set = special;
                        clear = start;
                        if (start == special) clear++;
                    }
                    else {
                        if (start <= special) clear = special;
                        set = start;
                        if (start == special) set++;
                    }
                    if (bits.FindIndex(true, start) != set) return false;
                    if (bits.FindIndex(false, start) != clear) return false;
                }
                return bits.Length == length && bits.GetHashCode() == hash;
            }
            int main() {
                {
                    BitSet bits = new();
                    for (nuint length = 0; length <= 129; length++) {
                        bits.Resize(length);
                        int before = attempts;
                        int releasedBefore = releases;
                        deny = true;
                        bits.Clear();
                        if (!Check(&bits, 0, 0) || bits.CountSetBits() != 0) return 1;
                        if (bits.FindIndex(false) != 0 || bits.FindIndex(true) != length) return 2;
                        bits.SetAll(true);
                        if (!Check(&bits, 1, 0) || bits.CountSetBits() != length) return 3;
                        for (nuint hole = 0; hole < length; hole++) {
                            bits[hole] = false;
                            if (!Check(&bits, 3, hole) || bits.CountSetBits() != length - 1) return 4;
                            bits[hole] = true;
                        }
                        bits.Clear();
                        for (nuint bit = 0; bit < length; bit++) {
                            bits[bit] = true;
                            if (!Check(&bits, 2, bit) || bits.CountSetBits() != 1) return 5;
                            bits[bit] = false;
                        }
                        deny = false;
                        if (attempts != before || releases != releasedBefore) return 6;
                    }
                }
                if (allocations != releases) return 7;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResizeRangeAndShiftHistoriesMatchBitArrayAndInvalidStartsPreserveState(int optimization)
    {
        var random = new Random(719351);
        var model = new BitArray(0);
        using var packet = new MemoryStream();
        const int steps = 48;
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, leaveOpen: true))
        {
            for (int step = 0; step < steps; step++)
            {
                int operation = step % 6;
                int first = 0, second = 0;
                bool value = random.Next(2) != 0;
                if (operation == 0)
                {
                    int[] boundaries = [0, 65, 31, 64, 1, 33, 32, 65];
                    first = boundaries[step / 6];
                    model.Length = first;
                }
                else if (operation == 1) model.SetAll(value);
                else if (operation is 2 or 3)
                {
                    first = random.Next(model.Length + 1);
                    second = random.Next(model.Length - first + 1);
                    for (int index = first; index < first + second; index++)
                        model[index] = operation == 2 ? value : !model[index];
                }
                else
                {
                    first = random.Next(model.Length + 33);
                    var previous = new BitArray(model);
                    for (int index = 0; index < model.Length; index++)
                    {
                        int source = operation == 4 ? index - first : index + first;
                        model[index] = source >= 0 && source < previous.Length && previous[source];
                    }
                }

                writer.Write((byte)operation);
                writer.Write((ushort)first);
                writer.Write((ushort)second);
                writer.Write(value);
                writer.Write((ushort)model.Length);
                for (int index = 0; index < model.Length; index++) writer.Write(model[index]);
                for (int start = 0; start <= model.Length; start++)
                {
                    writer.Write((ushort)Find(model, true, start));
                    writer.Write((ushort)Find(model, false, start));
                }
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Allocator + $$$"""
            int Digit(char value) nothrow {
                if (value <= '9') return value - '0';
                return value - 'A' + 10;
            }
            int ReadByte(readonly(char)* packet, ref nuint position) nothrow {
                int value = (Digit(packet[position]) << 4) | Digit(packet[position + 1]);
                position += 2;
                return value;
            }
            nuint ReadSize(readonly(char)* packet, ref nuint position) nothrow {
                nuint value = (nuint)ReadByte(packet, ref position);
                return value + (nuint)ReadByte(packet, ref position) * 256;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                {
                    BitSet bits = new();
                    bool[129] expected;
                    for (int step = 0; step < {{{steps}}}; step++) {
                        int operation = ReadByte(packet, ref position);
                        nuint first = ReadSize(packet, ref position);
                        nuint second = ReadSize(packet, ref position);
                        bool value = ReadByte(packet, ref position) != 0;
                        if (operation == 0) bits.Resize(first);
                        else if (operation == 1) bits.SetAll(value);
                        else if (operation == 2) bits.SetRange(first, second, value);
                        else if (operation == 3) bits.FlipRange(first, second);
                        else if (operation == 4) bits.ShiftLeft(first);
                        else bits.ShiftRight(first);
                        nuint length = ReadSize(packet, ref position);
                        if (bits.Length != length) return 1;
                        for (nuint index = 0; index < length; index++) {
                            expected[index] = ReadByte(packet, ref position) != 0;
                            if (bits[index] != expected[index]) return 2;
                        }
                        int before = attempts;
                        int releasedBefore = releases;
                        nuint hash = bits.GetHashCode();
                        deny = true;
                        for (nuint start = 0; start <= length; start++) {
                            nuint set = ReadSize(packet, ref position);
                            nuint clear = ReadSize(packet, ref position);
                            if (bits.FindIndex(true, start) != set || bits.FindIndex(false, start) != clear) return 3;
                        }
                        deny = false;
                        if (attempts != before || releases != releasedBefore || bits.GetHashCode() != hash) return 4;
                        for (nuint index = 0; index < length; index++)
                            if (bits[index] != expected[index]) return 5;
                    }
                    nuint length = bits.Length;
                    nuint hash = bits.GetHashCode();
                    int live = allocations - releases;
                    for (int mode = 0; mode < 4; mode++) {
                        nuint invalid = length + 1;
                        if (mode >= 2) invalid = (nuint)0 - (nuint)1;
                        nuint result = 99;
                        bool caught = false;
                        try { result = bits.FindIndex(mode % 2 == 0, invalid); }
                        catch (ref Exception error) {
                            if (!CString::Equals(error.GetMessage(), "BitSet search start out of range")) return 6;
                            caught = true;
                        }
                        if (!caught || result != 99 || bits.Length != length || bits.GetHashCode() != hash) return 7;
                        if (allocations - releases != live) return 8;
                    }
                }
                if (position != {{{hex.Length}}} || allocations != releases) return 9;
                return 0;
            }
            """, optimization);
    }

    private static int Find(BitArray bits, bool value, int start)
    {
        for (int index = start; index < bits.Length; index++)
            if (bits[index] == value) return index;
        return bits.Length;
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("bitset-search.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"BitSet search exit {result.ExitCode}: {result.StandardError}");
    }
}
