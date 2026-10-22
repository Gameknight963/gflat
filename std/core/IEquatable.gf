namespace std;

// Primitive equality treats all NaNs as equal and signed zeros as equal.
interface IEquatable<T>
{
    immutable bool Equals(immutable(T)* other);

    implementation byte : IEquatable<byte>
    {
        public immutable bool Equals(readonly(byte)* other)
        {
            return *this == *other;
        }
    }

    implementation sbyte : IEquatable<sbyte>
    {
        public immutable bool Equals(readonly(sbyte)* other)
        {
            return *this == *other;
        }
    }

    implementation short : IEquatable<short>
    {
        public immutable bool Equals(readonly(short)* other)
        {
            return *this == *other;
        }
    }

    implementation ushort : IEquatable<ushort>
    {
        public immutable bool Equals(readonly(ushort)* other)
        {
            return *this == *other;
        }
    }

    implementation int : IEquatable<int>
    {
        public immutable bool Equals(readonly(int)* other)
        {
            return *this == *other;
        }
    }

    implementation uint : IEquatable<uint>
    {
        public immutable bool Equals(readonly(uint)* other)
        {
            return *this == *other;
        }
    }

    implementation long : IEquatable<long>
    {
        public immutable bool Equals(readonly(long)* other)
        {
            return *this == *other;
        }
    }

    implementation ulong : IEquatable<ulong>
    {
        public immutable bool Equals(readonly(ulong)* other)
        {
            return *this == *other;
        }
    }

    implementation nint : IEquatable<nint>
    {
        public immutable bool Equals(readonly(nint)* other)
        {
            return *this == *other;
        }
    }

    implementation nuint : IEquatable<nuint>
    {
        public immutable bool Equals(readonly(nuint)* other)
        {
            return *this == *other;
        }
    }

    implementation char : IEquatable<char>
    {
        public immutable bool Equals(readonly(char)* other)
        {
            return *this == *other;
        }
    }

    implementation extralong : IEquatable<extralong>
    {
        public immutable bool Equals(readonly(extralong)* other) => *this == *other;
    }

    implementation bool : IEquatable<bool>
    {
        public immutable bool Equals(readonly(bool)* other)
        {
            return *this == *other;
        }
    }

    implementation float : IEquatable<float>
    {
        public immutable bool Equals(readonly(float)* other)
        {
            return *this == *other || (*this != *this && *other != *other);
        }
    }

    implementation double : IEquatable<double>
    {
        public immutable bool Equals(readonly(double)* other)
        {
            return *this == *other || (*this != *this && *other != *other);
        }
    }

}
