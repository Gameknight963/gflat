using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class MathDivRemTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MATH_DIVREM.md", 0);

    private static readonly (string Name, int Bits, bool Signed)[] Types =
    [
        ("sbyte", 8, true), ("byte", 8, false), ("short", 16, true), ("ushort", 16, false),
        ("int", 32, true), ("uint", 32, false), ("long", 64, true), ("ulong", 64, false),
        ("extralong", 128, true), ("nint", IntPtr.Size * 8, true), ("nuint", IntPtr.Size * 8, false)
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryIntegerWidthMatchesBigIntegerIncludingInvalidInputsAndAliasedOutputs(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_divrem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "oracle.bin");
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var type in Types)
            {
                var cases = Cases(type.Bits, type.Signed).ToArray();
                writer.Write((ulong)cases.Length);
                BigInteger minimum = type.Signed ? -(BigInteger.One << (type.Bits - 1)) : BigInteger.Zero;
                BigInteger maximum = (BigInteger.One << (type.Bits - (type.Signed ? 1 : 0))) - 1;
                foreach (var (a, b) in cases)
                {
                    BigInteger q = 0, r = 0;
                    bool valid = b != 0;
                    if (valid)
                    {
                        q = BigInteger.DivRem(a, b, out r);
                        valid = q >= minimum && q <= maximum;
                        if (!valid) { q = 0; r = 0; }
                    }
                    foreach (BigInteger value in new[] { a, b, q, r })
                    {
                        BigInteger bits = value & ((BigInteger.One << 128) - 1);
                        writer.Write((ulong)(bits & ulong.MaxValue));
                        writer.Write((ulong)(bits >> 64));
                    }
                    writer.Write(valid ? 1ul : 0ul);
                }
            }
            var program = new StringBuilder("""
                using Standard;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fgetc(void* file);
                extern "C" int fclose(void* file);
                extern "C" int printf(readonly(char)* format, ...);
                extralong Wide(ulong low, ulong high) nothrow => ((extralong)high << 64) | (extralong)low;
                int Failed(readonly(char)* type, ulong record, int check)
                {
                    printf("%s record %llu check %d\n", type, record, check);
                    return check;
                }

                """);
            foreach (var type in Types) program.Append(Check(type.Name, type.Bits, type.Signed));
            program.Append("int main()\n{\n    void*? input = fopen(" + Literal(path) + ", \"rb\");\n    if (input == null) return 100;\n");
            program.Append("    defer { fclose(input!); }\n");
            foreach (var type in Types) program.Append($"    if (Check_{type.Name}(input!) != 0) return 101;\n");
            program.Append("    if (fgetc(input!) != -1) return 102;\n    return 0;\n}\n");
            string ir = Compiler.Emit(StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("divrem.gf", program.ToString())), options: new() { InternalizeFunctions = true });
            var result = CompilerTestHelper.RunIr(ir, optimization);
            Assert.True(result.ExitCode == 0, $"DivRem exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Check(string type, int bits, bool signed)
    {
        string minimum = signed ? $"({type})((extralong)1 << {bits - 1})" : $"({type})0";
        return $$"""
            int Check_{{type}}(void* input)
            {
                ulong count = 0ul;
                if (fread(&count, (nuint)8, (nuint)1, input) != (nuint)1) return 90;
                for (ulong index = 0ul; index < count; index++)
                {
                    ulong[9] record;
                    if (fread(&record[0], (nuint)8, (nuint)9, input) != (nuint)9) return 91;
                    {{type}} a = ({{type}})Wide(record[0], record[1]);
                    {{type}} b = ({{type}})Wide(record[2], record[3]);
                    {{type}} expectedQ = ({{type}})Wide(record[4], record[5]);
                    {{type}} expectedR = ({{type}})Wide(record[6], record[7]);
                    bool expected = record[8] != 0ul;
                    {{type}} q = ({{type}})17;
                    {{type}} r = ({{type}})19;
                    if (Math::TryDivRem(a, b, ref q, ref r) != expected || q != expectedQ || r != expectedR)
                        return Failed("{{type}}", index, 1);
                    {{type}} slot = ({{type}})17;
                    if (Math::TryDivRem(a, b, ref slot, ref slot) != expected || slot != expectedR)
                        return Failed("{{type}}", index, 2);
                    {{type}} aliasA = a;
                    {{type}} aliasB = b;
                    if (Math::TryDivRem(aliasA, aliasB, ref aliasA, ref aliasB) != expected || aliasA != expectedQ || aliasB != expectedR)
                        return Failed("{{type}}", index, 3);
                    if (expected)
                    {
                        r = ({{type}})19;
                        q = Math::DivRem(a, b, ref r);
                        if (q != expectedQ || r != expectedR) return Failed("{{type}}", index, 4);
                        aliasA = a;
                        if (Math::DivRem(aliasA, b, ref aliasA) != expectedQ || aliasA != expectedR)
                            return Failed("{{type}}", index, 5);
                        aliasB = b;
                        if (Math::DivRem(a, aliasB, ref aliasB) != expectedQ || aliasB != expectedR)
                            return Failed("{{type}}", index, 6);
                    }
                }
                {{type}} remainder = ({{type}})19;
                bool caught = false;
                try { Math::DivRem(({{type}})42, ({{type}})0, ref remainder); }
                catch (Exception* error) { caught = true; }
                if (!caught || remainder != ({{type}})19) return 92;
                {{(signed ? $$"""
                caught = false;
                try { Math::DivRem({{minimum}}, ({{type}})-1, ref remainder); }
                catch (Exception* error) { caught = true; }
                if (!caught || remainder != ({{type}})19) return 93;
                """ : "")}}
                return 0;
            }

            """;
    }

    private static IEnumerable<(BigInteger A, BigInteger B)> Cases(int bits, bool signed)
    {
        BigInteger minimum = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
        BigInteger maximum = (BigInteger.One << (bits - (signed ? 1 : 0))) - 1;
        if (bits == 8)
        {
            for (BigInteger a = minimum; a <= maximum; a++)
            for (BigInteger b = minimum; b <= maximum; b++) yield return (a, b);
            yield break;
        }
        BigInteger[] boundaries = [minimum, minimum + 1, -101, -8, -7, -3, -2, -1, 0, 1, 2, 3, 7, 8, 101, maximum - 1, maximum];
        var selected = boundaries.Where(n => n >= minimum && n <= maximum).Distinct().ToArray();
        foreach (BigInteger a in selected)
        foreach (BigInteger b in selected) yield return (a, b);
        var random = new Random(bits * 103 + (signed ? 1 : 0));
        BigInteger mask = (BigInteger.One << bits) - 1;
        for (int i = 0; i < 256; i++)
        {
            byte[] data = new byte[32];
            random.NextBytes(data);
            BigInteger a = new BigInteger(data.AsSpan(0, 16), isUnsigned: true) & mask;
            BigInteger b = new BigInteger(data.AsSpan(16, 16), isUnsigned: true) & mask;
            if (signed && a > maximum) a -= BigInteger.One << bits;
            if (signed && b > maximum) b -= BigInteger.One << bits;
            yield return (a, b);
        }
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
