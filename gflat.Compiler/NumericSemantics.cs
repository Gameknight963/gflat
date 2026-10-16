using System.Numerics;
using gflat.comptime;

namespace gflat;

public static class NumericSemantics
{
    public static (int Bits, bool Signed)? IntegerType(string name) => name switch
    {
        "byte" => (8, false), "char" or "sbyte" => (8, true),
        "short" => (16, true), "ushort" => (16, false),
        "int" => (32, true), "uint" => (32, false),
        "long" => (64, true), "ulong" => (64, false),
        "nint" => (TargetInfo.Default.PointerBits, true),
        "nuint" => (TargetInfo.Default.PointerBits, false),
        "extralong" => (128, true),
        _ => null
    };

    public static ConstValue Cast(ConstValue value, string target, int line)
    {
        if (target is "float" or "double")
        {
            double number;
            if (ConstValue.TryIntegerValue(value, out var integerInput))
                number = RoundInteger(integerInput, target == "float" ? 24 : 53);
            else if (value is ConstValue.Float floating) number = floating.Value;
            else throw new ConstEvalException("Expected a numeric constant", line);
            return new ConstValue.Float(target == "float" ? (float)number : number);
        }
        if (IntegerType(target) is not { } type)
            throw new ConstEvalException($"Unsupported constant conversion to '{target}'", line);
        BigInteger integer = value switch
        {
            ConstValue.Integer i => i.Value, ConstValue.UInteger u => u.Value,
            ConstValue.Char c => unchecked((sbyte)c.Value),
            ConstValue.WideInteger wide => wide.Value,
            ConstValue.Float f when double.IsFinite(f.Value) => new BigInteger(Math.Truncate(f.Value)),
            _ => throw new ConstEvalException("Expected a finite numeric constant", line)
        };
        BigInteger modulus = BigInteger.One << type.Bits;
        if (value is ConstValue.Float)
        {
            BigInteger minimum = type.Signed ? -(modulus >> 1) : 0;
            BigInteger maximum = type.Signed ? (modulus >> 1) - 1 : modulus - 1;
            if (integer < minimum || integer > maximum)
                throw new ConstEvalException("Floating-point conversion is outside the integer range", line);
        }
        integer = ((integer % modulus) + modulus) % modulus;
        if (target == "char") return new ConstValue.Char((char)(byte)integer);
        if (type.Signed && integer >= (modulus >> 1)) integer -= modulus;
        if (type.Bits > 64) return new ConstValue.WideInteger(integer);
        return type.Signed ? new ConstValue.Integer((long)integer) : new ConstValue.UInteger((ulong)integer);
    }

    private static double RoundInteger(BigInteger value, int precision)
    {
        bool negative = value.Sign < 0;
        BigInteger magnitude = BigInteger.Abs(value);
        int discardedBits = (int)magnitude.GetBitLength() - precision;
        if (discardedBits <= 0) return (double)value;

        // Round the exact integer directly to the destination significand.
        // Going through double first can turn an above-halfway integer into
        // an exact tie and incorrectly round a subsequent float conversion.
        BigInteger significand = magnitude >> discardedBits;
        BigInteger remainder = magnitude - (significand << discardedBits);
        BigInteger halfway = BigInteger.One << (discardedBits - 1);
        if (remainder > halfway || remainder == halfway && !significand.IsEven) significand++;
        double rounded = Math.ScaleB((double)significand, discardedBits);
        return negative ? -rounded : rounded;
    }
}
