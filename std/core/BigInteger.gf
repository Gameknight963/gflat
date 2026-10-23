namespace std;

// Decimal-oriented signed integer. Limbs are least-significant first in base
// 1,000,000,000; zero has no limbs and sign zero. Arithmetic returns new owners.
public struct BigInteger : IFormattable, IStringConvertable, IEquatable<BigInteger>, IComparable<BigInteger>, IHashable
{
    private Vector<uint> limbs;
    private int sign;
    private static const ulong Radix = 1000000000ul;

    public BigInteger()
    {
        limbs = new();
        sign = 0;
    }

    public BigInteger(long value)
    {
        limbs = new();
        sign = 0;
        ulong magnitude = (ulong)value;
        if (value < 0)
        {
            sign = -1;
            magnitude = 0ul - magnitude;
        }
        else if (value > 0) sign = 1;
        while (magnitude != 0ul)
        {
            limbs.Add((uint)(magnitude % Radix));
            magnitude /= Radix;
        }
    }

    public copy BigInteger(immutable(BigInteger)* source)
    {
        limbs = new();
        sign = source.sign;
        limbs.Reserve(source.limbs.Count);
        for (nuint i = 0; i < source.limbs.Count; i++) limbs.Add(source.limbs[i]);
    }

    public int Sign { immutable get nothrow => sign; }

    private void Normalize()
    {
        while (limbs.Count != 0 && limbs[limbs.Count - 1] == 0u) limbs.Truncate(limbs.Count - 1);
        if (limbs.Count == 0) sign = 0;
    }

    private static int CompareMagnitude(immutable(BigInteger)* first, immutable(BigInteger)* second)
    {
        if (first.limbs.Count < second.limbs.Count) return -1;
        if (first.limbs.Count > second.limbs.Count) return 1;
        nuint i = first.limbs.Count;
        while (i != 0)
        {
            i--;
            if (first.limbs[i] < second.limbs[i]) return -1;
            if (first.limbs[i] > second.limbs[i]) return 1;
        }
        return 0;
    }

    public immutable int CompareTo(immutable(BigInteger)* other)
    {
        if (sign < other.sign) return -1;
        if (sign > other.sign) return 1;
        return sign * BigInteger::CompareMagnitude(this, other);
    }

    public immutable bool Equals(immutable(BigInteger)* other) => this.CompareTo(other) == 0;
    public immutable nuint GetHashCode()
    {
        nuint hash = Hashing::Integer((ulong)sign);
        for (nuint i = 0; i < limbs.Count; i++) hash = Hashing::Combine(hash, Hashing::Integer((ulong)limbs[i]));
        return hash;
    }

    private static BigInteger AddMagnitude(immutable(BigInteger)* first, immutable(BigInteger)* second, int resultSign)
    {
        BigInteger result = new();
        result.sign = resultSign;
        nuint count = first.limbs.Count;
        if (second.limbs.Count > count) count = second.limbs.Count;
        if (count == (nuint)0 - (nuint)1) throw new Exception("BigInteger size overflow");
        result.limbs.Reserve(count + 1);
        ulong carry = 0;
        for (nuint i = 0; i < count; i++)
        {
            ulong sum = carry;
            if (i < first.limbs.Count) sum += (ulong)first.limbs[i];
            if (i < second.limbs.Count) sum += (ulong)second.limbs[i];
            result.limbs.Add((uint)(sum % Radix));
            carry = sum / Radix;
        }
        if (carry != 0ul) result.limbs.Add((uint)carry);
        return move result;
    }
    // Magnitudes must satisfy first >= second.
    private static BigInteger SubtractMagnitude(immutable(BigInteger)* first, immutable(BigInteger)* second, int resultSign)
    {
        BigInteger result = new();
        result.sign = resultSign;
        result.limbs.Reserve(first.limbs.Count);
        long borrow = 0;
        for (nuint i = 0; i < first.limbs.Count; i++)
        {
            long difference = (long)first.limbs[i] - borrow;
            if (i < second.limbs.Count) difference -= (long)second.limbs[i];
            borrow = 0;
            if (difference < 0)
            {
                difference += (long)Radix;
                borrow = 1;
            }
            result.limbs.Add((uint)difference);
        }
        result.Normalize();
        return move result;
    }

    public immutable BigInteger Add(immutable(BigInteger)* other)
    {
        if (sign == 0) return *other;
        if (other.sign == 0) return *this;
        if (sign == other.sign) return BigInteger::AddMagnitude(this, other, sign);
        int comparison = BigInteger::CompareMagnitude(this, other);
        if (comparison == 0) return new BigInteger();
        if (comparison > 0) return BigInteger::SubtractMagnitude(this, other, sign);
        return BigInteger::SubtractMagnitude(other, this, other.sign);
    }

    public immutable BigInteger Subtract(immutable(BigInteger)* other)
    {
        if (other.sign == 0) return *this;
        if (sign == 0)
        {
            BigInteger result = *other;
            result.sign = -result.sign;
            return move result;
        }
        if (sign != other.sign) return BigInteger::AddMagnitude(this, other, sign);
        int comparison = BigInteger::CompareMagnitude(this, other);
        if (comparison == 0) return new BigInteger();
        if (comparison > 0) return BigInteger::SubtractMagnitude(this, other, sign);
        return BigInteger::SubtractMagnitude(other, this, -sign);
    }

    public immutable BigInteger Multiply(immutable(BigInteger)* other)
    {
        BigInteger result = new();
        if (sign == 0 || other.sign == 0) return move result;
        nuint count = limbs.Count;
        if (other.limbs.Count > (nuint)0 - (nuint)1 - count) throw new Exception("BigInteger size overflow");
        count += other.limbs.Count;
        result.limbs.Reserve(count);
        for (nuint i = 0; i < count; i++) result.limbs.Add(0);
        result.sign = sign * other.sign;
        for (nuint i = 0; i < limbs.Count; i++)
        {
            ulong carry = 0;
            for (nuint j = 0; j < other.limbs.Count; j++)
            {
                ulong product = (ulong)limbs[i] * (ulong)other.limbs[j] + (ulong)result.limbs[i + j] + carry;
                // Each accumulator is at most Radix*Radix - 1, below 10^18.
                result.limbs[i + j] = (uint)(product % Radix);
                carry = product / Radix;
            }
            result.limbs[i + other.limbs.Count] = (uint)carry;
        }
        result.Normalize();
        return move result;
    }

    public immutable BigInteger Pow(uint exponent)
    {
        if (exponent == 0u) return new BigInteger(1l);
        if (sign == 0) return new BigInteger();
        if (exponent == 1u) return *this;
        if (limbs.Count == 1 && limbs[0] == 1u)
        {
            if (sign < 0 && (exponent & 1u) != 0u) return new BigInteger(-1l);
            return new BigInteger(1l);
        }

        // Start at the leading one bit, already represented by this copy.
        // Reuse Multiply's fresh-result ownership and self-aliasing behavior.
        BigInteger result = *this;
        uint bit = 1u;
        while (bit <= exponent / 2u) bit = bit << 1;
        bit = bit >> 1;
        while (bit != 0u)
        {
            result = result.Multiply(&result);
            if ((exponent & bit) != 0u) result = result.Multiply(this);
            bit = bit >> 1;
        }
        return move result;
    }

    // Signed 32-bit divisor. Quotient truncates toward zero; remainder has dividend sign.
    public immutable BigInteger DivRem(int divisor, ref int remainder)
    {
        if (divisor == 0) throw new Exception("BigInteger divisor is zero");
        long widened = (long)divisor;
        if (widened < 0) widened = -widened;
        ulong magnitude = (ulong)widened;
        BigInteger result = new();
        result.sign = sign;
        if (divisor < 0) result.sign = -result.sign;
        nuint count = limbs.Count;
        result.limbs.Reserve(count);
        for (nuint i = 0; i < count; i++) result.limbs.Add(0u);
        ulong carry = 0ul;
        nuint index = count;
        while (index != 0)
        {
            index--;
            // carry < abs(divisor) <= 2^31, so current <= 2147483647999999999.
            ulong current = carry * Radix + (ulong)limbs[index];
            result.limbs[index] = (uint)(current / magnitude);
            carry = current % magnitude;
        }
        result.Normalize();
        int rest = (int)carry;
        if (sign < 0) rest = -rest;
        remainder = rest;
        return move result;
    }

    // Validate the complete syntax before allocating or replacing an old owner.
    private static bool ValidateDecimal(readonly(char)* text, nuint count, ref nuint start, ref int resultSign) nothrow
    {
        start = 0;
        resultSign = 1;
        if (count == 0) return false;
        if (text[0] == '-' || text[0] == '+')
        {
            if (text[0] == '-') resultSign = -1;
            start++;
        }
        if (start == count) return false;
        for (nuint i = start; i < count; i++) if (text[i] < '0' || text[i] > '9') return false;
        while (start < count && text[start] == '0') start++;
        return true;
    }

    // Input was validated and leading zeroes skipped. All input reads finish
    // before TryParse replaces output, including when old limb storage is input.
    private static BigInteger BuildDecimal(readonly(char)* text, nuint count, nuint start, int resultSign)
    {
        BigInteger result = new();
        if (start == count) return move result;
        result.sign = resultSign;
        nuint digits = count - start;
        nuint chunks = digits / 9;
        if (digits % 9 != 0) chunks++;
        result.limbs.Reserve(chunks);
        nuint end = count;
        while (end > start)
        {
            nuint beginning = start;
            if (end - start > 9) beginning = end - 9;
            uint chunk = 0;
            for (nuint i = beginning; i < end; i++) chunk = chunk * 10u + (uint)(text[i] - '0');
            result.limbs.Add(chunk);
            end = beginning;
        }
        return move result;
    }

    public static BigInteger Parse(readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return BigInteger::Parse(text, count);
    }

    public static BigInteger Parse(readonly(char)* text, nuint count)
    {
        nuint start = 0;
        int resultSign = 1;
        if (!BigInteger::ValidateDecimal(text, count, ref start, ref resultSign)) throw new Exception("Invalid BigInteger");
        return BigInteger::BuildDecimal(text, count, start, resultSign);
    }

    public static bool TryParse(readonly(char)* text, nuint count, ref BigInteger result)
    {
        nuint start = 0;
        int resultSign = 1;
        if (!BigInteger::ValidateDecimal(text, count, ref start, ref resultSign))
        {
            BigInteger zero = new();
            result = move zero;
            return false;
        }
        BigInteger parsed = BigInteger::BuildDecimal(text, count, start, resultSign);
        result = move parsed;
        return true;
    }

    public static bool TryParse(readonly(char)* text, ref BigInteger result)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return BigInteger::TryParse(text, count, ref result);
    }

    public immutable void Format(ITextWriter* output)
    {
        if (sign == 0)
        {
            output.Write("0", 1);
            return;
        }
        if (sign < 0) output.Write("-", 1);
        nuint i = limbs.Count - 1;
        std::Format(limbs[i], output);
        char[9] chunk;
        while (i != 0)
        {
            i--;
            uint value = limbs[i];
            nuint position = 9;
            while (position != 0)
            {
                position--;
                chunk[position] = (char)('0' + value % 10u);
                value /= 10u;
            }
            output.Write(chunk, 9);
        }
    }

    public immutable String ToString()
    {
        String result = new();
        this.Format(&result);
        return move result;
    }
}
