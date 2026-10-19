namespace gflat.Tests;

public sealed class StandardMathTests
{
    private static void Run(string program)
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("Math.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "core", "Math.gf"))),
            new SourceFile("main.gf", program)
        ]);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("int")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    [InlineData("nint")]
    [InlineData("nuint")]
    public void IntegerOverloadsReturnTheirInputTypeAndValidateClampBounds(string type)
    {
        Run($$"""
            using std;
            int main() {
                {{type}} minimum = ({{type}})2; {{type}} maximum = ({{type}})8;
                {{type}} a = Math::Min(maximum, minimum);
                {{type}} b = Math::Max(minimum, maximum);
                {{type}} low = Math::Clamp(({{type}})0, minimum, maximum);
                {{type}} high = Math::Clamp(({{type}})9, minimum, maximum);
                {{type}} middle = Math::Clamp(({{type}})5, minimum, maximum);
                if (a != minimum || b != maximum || low != minimum || high != maximum || middle != ({{type}})5) return 1;
                if (Math::Clamp(minimum, minimum, minimum) != minimum) return 2;
                try { Math::Clamp(({{type}})5, maximum, minimum); return 3; } catch (Exception* error) { }
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("sbyte", "(sbyte)-128")]
    [InlineData("short", "(short)-32768")]
    [InlineData("int", "-2147483647 - 1")]
    [InlineData("long", "-9223372036854775807l - 1l")]
    [InlineData("nint", "(nint)((nuint)1 << ((int)sizeof(nint) * 8 - 1))")]
    public void SignedAbsoluteValuesRejectUnrepresentableMinimum(string type, string minimum)
    {
        Run($$"""
            using std;
            int main() {
                {{type}} minimum = {{minimum}};
                {{type}} zero = Math::Abs(({{type}})0);
                {{type}} positive = Math::Abs(({{type}})7);
                {{type}} negative = Math::Abs(({{type}})-7);
                if (zero != ({{type}})0 || positive != ({{type}})7 || negative != ({{type}})7) return 1;
                if (Math::Min(minimum, ({{type}})0) != minimum || Math::Max(minimum, ({{type}})0) != ({{type}})0) return 2;
                try { Math::Abs(minimum); return 3; } catch (Exception* error) { }
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("float", "uint", "(uint)2147483648", "(uint)2139095040", "(uint)4290773009", "(uint)2143289361", "1f")]
    [InlineData("double", "ulong", "9223372036854775808ul", "9218868437227405312ul", "18444492273895866385ul", "9221120237041090577ul", "1.0")]
    public void FloatingClassificationAbsNaNsAndSignedZerosHaveDefinedSemantics(string type, string bitsType,
        string negativeZeroBits, string infinityBits, string negativeNaNBits, string positiveNaNBits, string one)
    {
        Run($$"""
            using std;
            {{type}} FromBits({{bitsType}} bits) => *(({{type}}*)&bits);
            {{bitsType}} Bits({{type}} value) => *(({{bitsType}}*)&value);
            int main() {
                {{type}} negativeZero = FromBits({{negativeZeroBits}});
                {{type}} positiveZero = ({{type}})0;
                {{type}} infinity = FromBits({{infinityBits}});
                {{type}} negativeNaN = FromBits({{negativeNaNBits}});
                if (Math::IsNaN(infinity) || Math::IsFinite(infinity) || !Math::IsNaN(negativeNaN) || Math::IsFinite(negativeNaN)) return 1;
                if (!Math::IsFinite({{one}}) || !Math::IsFinite(negativeZero) || Math::IsNaN({{one}})) return 2;
                if (Bits(Math::Abs(negativeZero)) != ({{bitsType}})0 || Bits(Math::Abs(negativeNaN)) != {{positiveNaNBits}}) return 3;
                if (Bits(Math::Min(positiveZero, negativeZero)) != {{negativeZeroBits}} || Bits(Math::Min(negativeZero, positiveZero)) != {{negativeZeroBits}}) return 4;
                if (Bits(Math::Max(positiveZero, negativeZero)) != ({{bitsType}})0 || Bits(Math::Max(negativeZero, positiveZero)) != ({{bitsType}})0) return 5;
                if (!Math::IsNaN(Math::Min(negativeNaN, {{one}})) || !Math::IsNaN(Math::Min({{one}}, negativeNaN))) return 6;
                if (!Math::IsNaN(Math::Max(negativeNaN, {{one}})) || !Math::IsNaN(Math::Max({{one}}, negativeNaN))) return 7;
                if (Math::Min(infinity, {{one}}) != {{one}} || Math::Max(infinity, {{one}}) != infinity) return 8;
                if (Math::Abs(-{{one}}) != {{one}}) return 9;
                return 0;
            }
            """);
    }
}
