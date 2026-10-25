namespace std;

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

    private static bool IsOne(immutable(BigInteger)* value) nothrow
    {
        long converted = 0l;
        return value.TryToLong(ref converted) && converted == 1l;
    }
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
        if (IsOne(&divisor)) return;
        BigInteger first = numerator.Divide(&divisor);
        BigInteger second = denominator.Divide(&divisor);
        numerator = move first; denominator = move second;
    }

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

    public immutable void Format(ITextWriter* output)
    {
        numerator.Format(output);
        if (!IsOne(&denominator)) { output.Write("/", 1); denominator.Format(output); }
    }
}
