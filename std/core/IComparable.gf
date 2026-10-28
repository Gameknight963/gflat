namespace std;

/// Ordering contract used by Comparer and default ordering algorithms.
/// Primitive float/double implementations order all NaNs before non-NaNs.
/// All NaNs compare equivalent to each other; both signed zeros compare equivalent.
interface IComparable<T>
{
    /// Returns a negative value, zero, or a positive value for less, equivalent, or greater.
    /// Borrows other without ownership transfer; zero defines ordering equivalence.
    immutable int CompareTo(immutable(T)* other);

    implementation byte : IComparable<byte>
    {
        public immutable int CompareTo(readonly(byte)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation sbyte : IComparable<sbyte>
    {
        public immutable int CompareTo(readonly(sbyte)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation short : IComparable<short>
    {
        public immutable int CompareTo(readonly(short)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation ushort : IComparable<ushort>
    {
        public immutable int CompareTo(readonly(ushort)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation int : IComparable<int>
    {
        public immutable int CompareTo(readonly(int)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation uint : IComparable<uint>
    {
        public immutable int CompareTo(readonly(uint)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation long : IComparable<long>
    {
        public immutable int CompareTo(readonly(long)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation ulong : IComparable<ulong>
    {
        public immutable int CompareTo(readonly(ulong)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation nint : IComparable<nint>
    {
        public immutable int CompareTo(readonly(nint)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation nuint : IComparable<nuint>
    {
        public immutable int CompareTo(readonly(nuint)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation char : IComparable<char>
    {
        public immutable int CompareTo(readonly(char)* other)
        {
            if ((byte)*this < (byte)*other) return -1;
            if ((byte)*this > (byte)*other) return 1;
            return 0;
        }
    }

    implementation extralong : IComparable<extralong>
    {
        public immutable int CompareTo(readonly(extralong)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation bool : IComparable<bool>
    {
        public immutable int CompareTo(readonly(bool)* other)
        {
            if (*this == *other) return 0;
            if (*this) return 1;
            return -1;
        }
    }

    implementation float : IComparable<float>
    {
        public immutable int CompareTo(readonly(float)* other)
        {
            // NaNs sort before every non-NaN, including negative infinity.
            if (*this != *this)
            {
                if (*other != *other) return 0;
                return -1;
            }
            if (*other != *other) return 1;
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation double : IComparable<double>
    {
        public immutable int CompareTo(readonly(double)* other)
        {
            // NaNs sort before every non-NaN, including negative infinity.
            if (*this != *this)
            {
                if (*other != *other) return 0;
                return -1;
            }
            if (*other != *other) return 1;
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

}
