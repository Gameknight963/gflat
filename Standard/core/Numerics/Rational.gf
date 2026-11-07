using Standard::Memory;

namespace Standard::Numerics;

// Exact canonical fraction: positive denominator, coprime parts, zero is 0/1.
public struct Rational : IEquatable<Rational>, IComparable<Rational>, IHashable, IFormattable
{
    private BigInteger numerator;
    private BigInteger denominator;

    public Rational() { numerator = new(); denominator = new(1l); }
    public Rational(long value) { numerator = new(value); denominator = new(1l); }
    public Rational(long numerator, long denominator)
    {
        this.numerator = new(numerator);
        this.denominator = new(denominator);
        this.Normalize();
    }
    public Rational(BigInteger numerator, BigInteger denominator)
    {
        this.numerator = move numerator;
        this.denominator = move denominator;
        this.Normalize();
    }
    public copy Rational(immutable(Rational)* source)
    {
        numerator = source.numerator;
        denominator = source.denominator;
    }

    private static bool IsOneValue(immutable(BigInteger)* value) nothrow => value.IsOne;
    // Called only while constructing a new owner, never on an exposed value.
    private void Normalize()
    {
        if (denominator.Sign == 0) throw new Exception("Rational denominator is zero");
        if (numerator.Sign == 0) { denominator = new(1l); return; }
        if (denominator.Sign < 0)
        {
            BigInteger first = numerator.Negate();
            BigInteger second = denominator.Negate();
            numerator = move first; denominator = move second;
        }
        BigInteger divisor = BigInteger::GreatestCommonDivisor(&numerator, &denominator);
        if (IsOneValue(&divisor)) return;
        BigInteger first = numerator.Divide(&divisor);
        BigInteger second = denominator.Divide(&divisor);
        numerator = move first; denominator = move second;
    }

    public int Sign { immutable get nothrow => numerator.Sign; }
    public bool IsZero { immutable get nothrow => numerator.IsZero; }
    public bool IsOne { immutable get nothrow => numerator.IsOne && denominator.IsOne; }
    public bool IsInteger { immutable get nothrow => denominator.IsOne; }

    // Ordinary owning value copies; these getters may allocate and throw.
    public BigInteger Numerator { immutable get => numerator; }
    public BigInteger Denominator { immutable get => denominator; }

    public immutable Rational Add(immutable(Rational)* other)
    {
        BigInteger first = numerator.Multiply(&other.denominator);
        BigInteger second = other.numerator.Multiply(&denominator);
        BigInteger top = first.Add(&second);
        BigInteger bottom = denominator.Multiply(&other.denominator);
        return new Rational(move top, move bottom);
    }
    public immutable Rational Subtract(immutable(Rational)* other)
    {
        BigInteger first = numerator.Multiply(&other.denominator);
        BigInteger second = other.numerator.Multiply(&denominator);
        BigInteger top = first.Subtract(&second);
        BigInteger bottom = denominator.Multiply(&other.denominator);
        return new Rational(move top, move bottom);
    }
    public immutable Rational Multiply(immutable(Rational)* other)
    {
        BigInteger top = numerator.Multiply(&other.numerator);
        BigInteger bottom = denominator.Multiply(&other.denominator);
        return new Rational(move top, move bottom);
    }
    public immutable Rational Divide(immutable(Rational)* other)
    {
        if (other.numerator.Sign == 0) throw new Exception("Rational divisor is zero");
        BigInteger top = numerator.Multiply(&other.denominator);
        BigInteger bottom = denominator.Multiply(&other.numerator);
        return new Rational(move top, move bottom);
    }
    public immutable Rational Negate()
    {
        BigInteger top = numerator.Negate();
        BigInteger bottom = denominator;
        return new Rational(move top, move bottom);
    }
    public static Rational Min(immutable(Rational)* first, immutable(Rational)* second)
    {
        immutable(Rational)* selected = first;
        if (first.CompareTo(second) > 0) selected = second;
        Rational result = *selected;
        return move result;
    }

    public static Rational Max(immutable(Rational)* first, immutable(Rational)* second)
    {
        immutable(Rational)* selected = first;
        if (first.CompareTo(second) < 0) selected = second;
        Rational result = *selected;
        return move result;
    }

    public static Rational Clamp(immutable(Rational)* value, immutable(Rational)* minimum, immutable(Rational)* maximum)
    {
        if (minimum.CompareTo(maximum) > 0) throw new Exception("Rational minimum exceeds maximum");
        immutable(Rational)* selected = value;
        if (value.CompareTo(minimum) < 0) selected = minimum;
        else if (value.CompareTo(maximum) > 0) selected = maximum;
        Rational result = *selected;
        return move result;
    }

    public static Rational CopySign(immutable(Rational)* value, immutable(Rational)* sign)
    {
        if ((value.Sign < 0) != (sign.Sign < 0)) return value.Negate();
        Rational result = *value;
        return move result;
    }

    public immutable Rational Abs()
    {
        if (numerator.Sign < 0) return this.Negate();
        return *this;
    }

    public immutable Rational Reciprocal()
    {
        if (numerator.IsZero) throw new Exception("Rational reciprocal of zero");
        return new Rational(denominator, numerator);
    }

    public immutable Rational Pow(int exponent)
    {
        if (exponent == 0) return new Rational(1l);
        if (exponent < 0 && numerator.IsZero) throw new Exception("Zero cannot have a negative rational power");
        uint magnitude = (uint)exponent;
        if (exponent < 0) magnitude = (uint)(-(long)exponent);
        BigInteger top = numerator.Pow(magnitude);
        BigInteger bottom = denominator.Pow(magnitude);
        if (exponent < 0) return new Rational(move bottom, move top);
        return new Rational(move top, move bottom);
    }

    public immutable BigInteger Truncate() => numerator.Divide(&denominator);

    private immutable BigInteger IntegralBound(bool upper)
    {
        BigInteger remainder = new();
        BigInteger quotient = numerator.DivRem(&denominator, ref remainder);
        if (!remainder.IsZero && ((upper && numerator.Sign > 0) || (!upper && numerator.Sign < 0)))
        {
            BigInteger one = new(1l);
            if (upper) return quotient.Add(&one);
            return quotient.Subtract(&one);
        }
        return move quotient;
    }

    public immutable BigInteger Floor() => this.IntegralBound(false);
    public immutable BigInteger Ceiling() => this.IntegralBound(true);

    public immutable int CompareTo(immutable(Rational)* other)
    {
        BigInteger first = numerator.Multiply(&other.denominator);
        BigInteger second = other.numerator.Multiply(&denominator);
        return first.CompareTo(&second);
    }
    public immutable bool Equals(immutable(Rational)* other)
        => numerator.Equals(&other.numerator) && denominator.Equals(&other.denominator);
    public immutable nuint GetHashCode()
        => Hashing::Combine(numerator.GetHashCode(), denominator.GetHashCode());

    private static bool TryReadParts(readonly(char)* text, nuint count,
        ref BigInteger numerator, ref BigInteger denominator)
    {
        nuint separator = count;
        for (nuint index = 0; index < count; index++)
        {
            if (text[index] == '/')
            {
                if (separator != count) return false;
                separator = index;
            }
        }
        if (!BigInteger::TryParse(text, separator, ref numerator)) return false;
        if (separator == count) denominator = new(1l);
        else if (!BigInteger::TryParse(text + separator + 1, count - separator - 1, ref denominator)
            || denominator.Sign == 0) return false;
        return true;
    }

    // All input reads and candidate construction finish before replacing result.
    // False assigns canonical zero; constructing that owner can itself throw.
    public static bool TryParse(readonly(char)* text, nuint count, ref Rational result)
    {
        BigInteger numerator = new();
        BigInteger denominator = new();
        if (!TryReadParts(text, count, ref numerator, ref denominator))
        {
            Rational zero = new();
            result = move zero;
            return false;
        }
        Rational parsed = new(move numerator, move denominator);
        result = move parsed;
        return true;
    }
    public static bool TryParse(readonly(char)* text, ref Rational result)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return TryParse(text, count, ref result);
    }
    public static Rational Parse(readonly(char)* text, nuint count)
    {
        BigInteger numerator = new();
        BigInteger denominator = new();
        if (!TryReadParts(text, count, ref numerator, ref denominator))
            throw new Exception("Invalid rational");
        return new Rational(move numerator, move denominator);
    }
    public static Rational Parse(readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return Parse(text, count);
    }

    public static Rational Parse(Span<readonly(char)> text)
    {
        if (text.IsEmpty) return Parse("", 0);
        return Parse(text.Data!, text.Length);
    }

    public static bool TryParse(Span<readonly(char)> text, ref Rational result)
    {
        if (text.IsEmpty) return TryParse("", 0, ref result);
        return TryParse(text.Data!, text.Length, ref result);
    }

    // Output and written must be disjoint from each other, this owner, and its numeric storage.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint top = 0;
        if (!numerator.TryGetDecimalLength(capacity, ref top)) return false;
        bool fraction = !denominator.IsOne;
        nuint bottom = 0;
        if (fraction)
        {
            if (top == capacity) return false;
            if (!denominator.TryGetDecimalLength(capacity - top - 1, ref bottom)) return false;
        }
        nuint part = 0;
        numerator.TryFormat(output, top, ref part);
        written = top;
        if (fraction)
        {
            output[top] = '/';
            denominator.TryFormat(output + top + 1, bottom, ref part);
            written += bottom + 1;
        }
        return true;
    }

    public immutable bool TryFormat(Span<char> destination, ref nuint written) nothrow
    {
        if (destination.IsEmpty) { written = 0; return false; }
        return this.TryFormat(destination.Data!, destination.Length, ref written);
    }

    public immutable void Format(ITextWriter* output)
    {
        numerator.Format(output);
        if (!IsOneValue(&denominator)) { output.Write("/", 1); denominator.Format(output); }
    }
}
