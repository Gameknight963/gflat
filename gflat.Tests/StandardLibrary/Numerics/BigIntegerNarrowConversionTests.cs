using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerNarrowConversionTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_NARROW_CONVERSIONS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntegerWidthsMatchExactRangesWithAllocationFreeTryAndCheckedAssignments(int optimization)
    {
        (string Type, string Method, int Bits, bool Signed)[] types = [("sbyte", "SByte", 8, true), ("byte", "Byte", 8, false),
            ("short", "Short", 16, true), ("ushort", "UShort", 16, false), ("int", "Int", 32, true),
            ("uint", "UInt", 32, false), ("nint", "NInt", IntPtr.Size * 8, true), ("nuint", "NUInt", IntPtr.Size * 8, false)];
        var values = new SortedSet<BigInteger> { 0, -1, 1, BigInteger.One << 256, -(BigInteger.One << 256) };
        var program = new StringBuilder("""
            using Standard;
            using Standard::Numerics;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            """);
        foreach (var (type, method, bits, signed) in types)
        {
            BigInteger minimum = signed ? -(BigInteger.One << (bits - 1)) : 0;
            BigInteger maximum = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
            foreach (BigInteger boundary in new[] { minimum, maximum })
            foreach (int delta in new[] { -1, 0, 1 }) values.Add(boundary + delta);
            program.AppendLine($$"""
                int Check{{method}}(immutable(BigInteger)* value, bool fits, {{type}} expected)
                {
                    {{type}} result = ({{type}})73;
                    int before = attempts;
                    deny = true;
                    bool success = value.TryTo{{method}}(ref result);
                    deny = false;
                    if (success != fits || result != expected || attempts != before) return 1;
                    {{type}} target = ({{type}})73;
                    bool caught = false;
                    try { target = value.To{{method}}(); }
                    catch (Exception* error) { caught = true; }
                    if (caught == fits) return 2;
                    if (fits) { if (target != expected) return 3; }
                    else if (target != ({{type}})73) return 4;
                    return 0;
                }
                """);
        }
        for (int bit = 0; bit <= 64; bit++)
        { values.Add(BigInteger.One << bit); values.Add(-(BigInteger.One << bit)); }
        var random = new Random(763419);
        for (int i = 0; i < 32; i++) { byte[] bytes = new byte[16]; random.NextBytes(bytes); values.Add(new BigInteger(bytes)); }
        program.AppendLine("int main() {");
        foreach (BigInteger value in values)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            program.AppendLine($"{{ BigInteger source = BigInteger::Parse(\"{text}\");");
            foreach (var (type, method, bits, signed) in types)
            {
                BigInteger minimum = signed ? -(BigInteger.One << (bits - 1)) : 0;
                BigInteger maximum = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
                bool fits = value >= minimum && value <= maximum;
                BigInteger expectedBits = fits ? value & ulong.MaxValue : 0;
                program.AppendLine($"if (Check{method}(&source, {fits.ToString().ToLowerInvariant()}, ({type})0x{expectedBits.ToString("x", CultureInfo.InvariantCulture)}ul) != 0) return 10;");
            }
            program.AppendLine($"String after = source.ToString(); if (!after.Equals(\"{text}\")) return 11; }} if (live != 0) return 12;");
        }
        program.AppendLine("return live; }");
        var result = CompilerTestHelper.Run(StandardTestSources.Numerics.Append(new SourceFile("narrow-bigint-conversions.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
