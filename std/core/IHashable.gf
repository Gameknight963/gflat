namespace std;

/// Hashing contract used by default hash collections.
/// Pair with consistent equality when using a custom key type.
interface IHashable
{
    /// Returns a hash consistent with equality; unequal values may share a hash.
    /// Hash codes are not persistent identifiers.
    immutable nuint GetHashCode();

    implementation byte
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation sbyte
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation short
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation ushort
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation int
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation uint
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation long
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation ulong
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation nint
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation nuint
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation char
    {
        public immutable nuint GetHashCode()
        {
            return Hashing::Integer((ulong)*this);
        }
    }

    implementation extralong
    {
        public immutable nuint GetHashCode()
            => Hashing::Integer((ulong)Hashing::Bytes(this.reinterpret<readonly(char)*>(), (nuint)sizeof(extralong)));
    }

    implementation bool
    {
        public immutable nuint GetHashCode()
        {
            if (*this) return (nuint)1;
            return (nuint)0;
        }
    }

    implementation float
    {
        public immutable nuint GetHashCode()
        {
            if (*this == 0.0f || *this != *this) return (nuint)0;
            return Hashing::Integer((ulong)(*this).reinterpret<uint>());
        }
    }

    implementation double
    {
        public immutable nuint GetHashCode()
        {
            if (*this == 0.0d || *this != *this) return (nuint)0;
            return Hashing::Integer((ulong)(*this).reinterpret<ulong>());
        }
    }

}
