namespace std;

// Primitive equality treats all NaNs as equal and signed zeros as equal.
interface IEquatable<T>
{
    readonly bool Equals(readonly(T)* other);

    implementation byte : IEquatable<byte>
    {
        public readonly bool Equals(readonly(byte)* other)
        {
            return *this == *other;
        }
    }

    implementation sbyte : IEquatable<sbyte>
    {
        public readonly bool Equals(readonly(sbyte)* other)
        {
            return *this == *other;
        }
    }

    implementation short : IEquatable<short>
    {
        public readonly bool Equals(readonly(short)* other)
        {
            return *this == *other;
        }
    }

    implementation ushort : IEquatable<ushort>
    {
        public readonly bool Equals(readonly(ushort)* other)
        {
            return *this == *other;
        }
    }

    implementation int : IEquatable<int>
    {
        public readonly bool Equals(readonly(int)* other)
        {
            return *this == *other;
        }
    }

    implementation uint : IEquatable<uint>
    {
        public readonly bool Equals(readonly(uint)* other)
        {
            return *this == *other;
        }
    }

    implementation long : IEquatable<long>
    {
        public readonly bool Equals(readonly(long)* other)
        {
            return *this == *other;
        }
    }

    implementation ulong : IEquatable<ulong>
    {
        public readonly bool Equals(readonly(ulong)* other)
        {
            return *this == *other;
        }
    }

    implementation nint : IEquatable<nint>
    {
        public readonly bool Equals(readonly(nint)* other)
        {
            return *this == *other;
        }
    }

    implementation nuint : IEquatable<nuint>
    {
        public readonly bool Equals(readonly(nuint)* other)
        {
            return *this == *other;
        }
    }

    implementation char : IEquatable<char>
    {
        public readonly bool Equals(readonly(char)* other)
        {
            return *this == *other;
        }
    }

    implementation extralong : IEquatable<extralong>
    {
        public readonly bool Equals(readonly(extralong)* other) => *this == *other;
    }

    implementation bool : IEquatable<bool>
    {
        public readonly bool Equals(readonly(bool)* other)
        {
            return *this == *other;
        }
    }

    implementation float : IEquatable<float>
    {
        public readonly bool Equals(readonly(float)* other)
        {
            return *this == *other || (*this != *this && *other != *other);
        }
    }

    implementation double : IEquatable<double>
    {
        public readonly bool Equals(readonly(double)* other)
        {
            return *this == *other || (*this != *this && *other != *other);
        }
    }

}
