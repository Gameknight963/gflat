namespace Standard::Numerics;

public BigInteger Round(immutable(Rational)* value, MidpointRounding mode = MidpointRounding::ToEven)
{
    int selected = (int)mode;
    if (selected < 0 || selected > 4) throw new Exception("Invalid midpoint rounding mode");
    if (mode == MidpointRounding::ToZero) return value.Truncate();
    if (mode == MidpointRounding::ToNegativeInfinity) return value.Floor();
    if (mode == MidpointRounding::ToPositiveInfinity) return value.Ceiling();
    if (value.IsInteger) return value.Truncate();
    BigInteger numerator = value.Numerator;
    BigInteger denominator = value.Denominator;
    BigInteger remainder = new();
    BigInteger quotient = numerator.DivRem(&denominator, ref remainder);
    BigInteger magnitude = remainder.Abs();
    BigInteger doubled = magnitude.Add(&magnitude);
    int comparison = doubled.CompareTo(&denominator);
    bool adjust = comparison > 0 || (comparison == 0 &&
        (mode == MidpointRounding::AwayFromZero || !quotient.IsEven));
    if (!adjust) return move quotient;
    BigInteger step = new((long)value.Sign);
    return quotient.Add(&step);
}

extension Rational
{
    public immutable BigInteger Round(MidpointRounding mode = MidpointRounding::ToEven)
        => Standard::Numerics::Round(this, mode);
}
