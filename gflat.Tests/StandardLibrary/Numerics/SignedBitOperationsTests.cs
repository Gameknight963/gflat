namespace gflat.Tests.Std.Numerics;

public sealed class SignedBitOperationsTests
{
    private static readonly int[] Offsets = [int.MinValue, -257, -129, -128, -127, -65, -64, -63, -1, 0, 1, 31, 32, 63, 64, 65, 127, 128, 129, 257, int.MaxValue];

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SIGNED_BIT_OPERATIONS.md", 0);

    [Theory]
    [InlineData("int", 0)]
    [InlineData("int", 2)]
    [InlineData("long", 0)]
    [InlineData("long", 2)]
    [InlineData("nint", 0)]
    [InlineData("nint", 2)]
    [InlineData("extralong", 0)]
    [InlineData("extralong", 2)]
    public void SignedWidthsMatchDotNetAcrossAllBitsAndExtremeRotationOffsets(string type, int optimization)
    {
        int width = type switch { "int" => 32, "extralong" => 128, "nint" => IntPtr.Size * 8, _ => 64 };
        string path = Path.Combine(Path.GetTempPath(), "gflat_signed_bits_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (UInt128 bits in Inputs(width))
            {
                WriteBits(writer, bits);
                (int pop, int leading, int trailing, bool power) = Counts(bits, width);
                writer.Write((ulong)pop);
                writer.Write((ulong)leading);
                writer.Write((ulong)trailing);
                writer.Write(power ? 1ul : 0ul);
                foreach (int offset in Offsets)
                {
                    WriteBits(writer, Rotate(bits, width, offset, true));
                    WriteBits(writer, Rotate(bits, width, offset, false));
                }
            }
            int columns = 6 + Offsets.Length * 4;
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            string value = width == 128 ? "(extralong)row[0] | ((extralong)row[1] << 64)" : $"({type})row[0]";
            string leftBits = width == 128 ? "(ulong)left" : width == 32 ? "(ulong)(uint)left" : "(ulong)left";
            string rightBits = width == 128 ? "(ulong)right" : width == 32 ? "(ulong)(uint)right" : "(ulong)right";
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/BitOperations.gf")
                .Append(new SourceFile("signed-bits.gf", $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* data, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[{{Offsets.Length}}] offsets = [{{string.Join(", ", Offsets.Select(offset => offset == int.MinValue ? "-2147483647 - 1" : offset.ToString(System.Globalization.CultureInfo.InvariantCulture)))}}];
                    ulong[{{columns}}] row = default(ulong[{{columns}}]);
                    while (true)
                    {
                        nuint count = fread(&row[0], 1, {{columns * 8}}, file!);
                        if (count == 0) break;
                        if (count != {{columns * 8}}) return 2;
                        {{type}} value = {{value}};
                        if (BitOperations::PopCount(value) != (int)row[2]) return 3;
                        if (BitOperations::LeadingZeroCount(value) != (int)row[3]) return 4;
                        if (BitOperations::TrailingZeroCount(value) != (int)row[4]) return 5;
                        if (BitOperations::IsPowerOfTwo(value) != (row[5] != 0ul)) return 6;
                        for (nuint i = 0; i < {{Offsets.Length}}; i++)
                        {
                            {{type}} left = BitOperations::RotateLeft(value, offsets[i]);
                            {{type}} right = BitOperations::RotateRight(value, offsets[i]);
                            nuint at = 6 + i * 4;
                            if ({{leftBits}} != row[at] || {{rightBits}} != row[at + 2]) return 7;
                            {{(width == 128 ? "if ((ulong)(left >> 64) != row[at + 1] || (ulong)(right >> 64) != row[at + 3]) return 8;" : "")}}
                        }
                    }
                    if (ferror(file!) != 0) return 9;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Signed {type} bit operations exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WideBitOperationsNeedNoWideIntegerRuntimeOrExceptionBoundaries()
    {
        string ir = Compiler.Emit(StandardTestSources.Load("core/Numerics/BitOperations.gf")
            .Append(new SourceFile("signed-bits-probe.gf", """
            using Standard::Numerics;

            extralong Probe(extralong value, int offset) nothrow
            {
                extralong rotated = BitOperations::RotateLeft(value, offset);
                rotated = BitOperations::RotateRight(rotated, offset);
                return rotated + (extralong)BitOperations::PopCount(value) +
                    (extralong)BitOperations::LeadingZeroCount(value) + (extralong)BitOperations::TrailingZeroCount(value);
            }
            """)));
        Assert.False(NativeToolchain.RequiresWideIntegerRuntime(ir));
        var bodies = System.Text.RegularExpressions.Regex.Matches(ir,
            @"(?ms)^define [^\r\n]*@(?<name>[^\s(]+)\([^\r\n]*\{\r?\n(?<body>.*?)^\}")
            .Cast<System.Text.RegularExpressions.Match>()
            .Where(match => match.Groups["name"].Value == "gflat$Probe" ||
                match.Groups["name"].Value.Contains("BitOperations", StringComparison.Ordinal)).ToArray();
        Assert.Contains(bodies, match => match.Groups["name"].Value == "gflat$Probe");
        Assert.True(bodies.Length > 1);
        foreach (var body in bodies)
        {
            Assert.DoesNotContain("invoke ", body.Groups["body"].Value);
            Assert.DoesNotContain("Allocate", body.Groups["body"].Value);
        }
    }

    private static void WriteBits(BinaryWriter writer, UInt128 bits)
    {
        writer.Write(unchecked((ulong)bits));
        writer.Write((ulong)(bits >> 64));
    }

    private static (int Pop, int Leading, int Trailing, bool Power) Counts(UInt128 bits, int width)
    {
        if (width == 32)
        {
            int value = unchecked((int)(uint)bits);
            return (int.PopCount(value), int.LeadingZeroCount(value), int.TrailingZeroCount(value), int.IsPow2(value));
        }
        if (width == 64)
        {
            long value = unchecked((long)(ulong)bits);
            return ((int)long.PopCount(value), (int)long.LeadingZeroCount(value), (int)long.TrailingZeroCount(value), long.IsPow2(value));
        }
        Int128 wide = unchecked((Int128)bits);
        return ((int)Int128.PopCount(wide), (int)Int128.LeadingZeroCount(wide), (int)Int128.TrailingZeroCount(wide), Int128.IsPow2(wide));
    }

    private static UInt128 Rotate(UInt128 bits, int width, int offset, bool left)
    {
        if (width == 32)
        {
            int value = unchecked((int)(uint)bits);
            return unchecked((uint)(left ? int.RotateLeft(value, offset) : int.RotateRight(value, offset)));
        }
        if (width == 64)
        {
            long value = unchecked((long)(ulong)bits);
            return unchecked((ulong)(left ? long.RotateLeft(value, offset) : long.RotateRight(value, offset)));
        }
        Int128 wide = unchecked((Int128)bits);
        return unchecked((UInt128)(left ? Int128.RotateLeft(wide, offset) : Int128.RotateRight(wide, offset)));
    }

    private static IEnumerable<UInt128> Inputs(int width)
    {
        UInt128 mask = UInt128.MaxValue >> (128 - width);
        var inputs = new HashSet<UInt128> { 0, 1, mask };
        for (int bit = 0; bit < width; bit++)
        {
            UInt128 value = (UInt128)1 << bit;
            inputs.Add(value);
            inputs.Add(value - 1);
            inputs.Add((value + 1) & mask);
        }
        var random = new Random(971503);
        byte[] bytes = new byte[16];
        for (int i = 0; i < 256; i++)
        {
            random.NextBytes(bytes);
            inputs.Add((((UInt128)BitConverter.ToUInt64(bytes, 8) << 64) | BitConverter.ToUInt64(bytes)) & mask);
        }
        return inputs;
    }
}
