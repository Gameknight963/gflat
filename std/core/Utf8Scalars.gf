namespace std;

// Borrows counted UTF-8 bytes. Input must remain alive throughout enumeration.
public struct Utf8Scalars : IEnumerable<uint, Utf8Scalars::Enumerator>
{
    private readonly(char)*? data;
    private nuint length;

    public Utf8Scalars() nothrow { data = null; length = 0; }
    public Utf8Scalars(readonly(char)*? text, nuint count)
    {
        if (text == null && count != 0) throw new Exception("UTF-8 storage is null");
        data = text; length = count;
    }

    public nuint ByteLength { immutable get nothrow => length; }
    public immutable Utf8Scalars::Enumerator GetEnumerator()
        => new Utf8Scalars::Enumerator(data, length);

    public immutable Enumerable<uint, Utf8Scalars, Utf8Scalars::Enumerator> AsEnumerable()
        => new Enumerable<uint, Utf8Scalars, Utf8Scalars::Enumerator>(*this);

    public struct Enumerator : IEnumerator<uint>
    {
        private readonly(char)*? data;
        private nuint length;
        private nuint offset;
        private uint current;
        private bool valid;

        public Enumerator(readonly(char)*? text, nuint count)
        {
            if (text == null && count != 0) throw new Exception("UTF-8 storage is null");
            data = text; length = count; offset = 0; current = 0u; valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (offset == length) return false;
            uint scalar = 0u; nuint consumed = 0;
            if (!Utf8::TryDecode(data! + offset, length - offset, &scalar, &consumed))
                throw new Exception("Invalid or truncated UTF-8");
            offset += consumed; current = scalar; valid = true;
            return true;
        }

        public uint Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }
}
