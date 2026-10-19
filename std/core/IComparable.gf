namespace std;

// Primitive equality treats all NaNs as equal and signed zeros as equal.
interface IComparable<T>
{
    readonly int CompareTo(readonly(T)* other);

    implementation byte : IComparable<byte>
    {
        public readonly int CompareTo(readonly(byte)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation sbyte : IComparable<sbyte>
    {
        public readonly int CompareTo(readonly(sbyte)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation short : IComparable<short>
    {
        public readonly int CompareTo(readonly(short)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation ushort : IComparable<ushort>
    {
        public readonly int CompareTo(readonly(ushort)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation int : IComparable<int>
    {
        public readonly int CompareTo(readonly(int)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation uint : IComparable<uint>
    {
        public readonly int CompareTo(readonly(uint)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation long : IComparable<long>
    {
        public readonly int CompareTo(readonly(long)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation ulong : IComparable<ulong>
    {
        public readonly int CompareTo(readonly(ulong)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation nint : IComparable<nint>
    {
        public readonly int CompareTo(readonly(nint)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation nuint : IComparable<nuint>
    {
        public readonly int CompareTo(readonly(nuint)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation char : IComparable<char>
    {
        public readonly int CompareTo(readonly(char)* other)
        {
            if (*this < *other) return -1;
            if (*this > *other) return 1;
            return 0;
        }
    }

    implementation bool : IComparable<bool>
    {
        public readonly int CompareTo(readonly(bool)* other)
        {
            if (*this == *other) return 0;
            if (*this) return 1;
            return -1;
        }
    }

    implementation float : IComparable<float>
    {
        public readonly int CompareTo(readonly(float)* other)
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
        public readonly int CompareTo(readonly(double)* other)
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
