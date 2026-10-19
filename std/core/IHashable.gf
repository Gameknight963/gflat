namespace std;

// Equal values must produce equal hashes. Hashes are not persistent identifiers.
interface IHashable
{
    readonly nuint GetHashCode();

    implementation byte
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation sbyte
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation short
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation ushort
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation int
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation uint
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation long
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation ulong
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation nint
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation nuint
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation char
    {
        public readonly nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation bool
    {
        public readonly nuint GetHashCode()
        {
            if (*this) return (nuint)1;
            return (nuint)0;
        }
    }

    implementation float
    {
        public readonly nuint GetHashCode()
        {
            if (*this == 0.0f || *this != *this) return (nuint)0;
            return Hashing::Integer((ulong)*((readonly(uint)*)this));
        }
    }

    implementation double
    {
        public readonly nuint GetHashCode()
        {
            if (*this == 0.0d || *this != *this) return (nuint)0;
            return Hashing::Integer((ulong)*((readonly(ulong)*)this));
        }
    }

}
