namespace Standard;

// Hashing contract for default hash collections.
interface IHashable
{
    // Returns a hash for collection lookup. Equal values must have equal hashes.
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
            return Hashing::Integer((ulong)__llvm_module<uint>("define i32 @convert(float %value) alwaysinline nounwind {\n%bits = bitcast float %value to i32\nret i32 %bits\n}", "convert", *this));
        }
    }

    implementation double
    {
        public immutable nuint GetHashCode()
        {
            if (*this == 0.0d || *this != *this) return (nuint)0;
            return Hashing::Integer((ulong)__llvm_module<ulong>("define i64 @convert(double %value) alwaysinline nounwind {\n%bits = bitcast double %value to i64\nret i64 %bits\n}", "convert", *this));
        }
    }

}
