namespace std;

// Borrows counted UTF-16 host ushort units. Input must remain alive throughout enumeration.
public struct Utf16Scalars : IEnumerable<uint, Utf16Scalars::Enumerator>
{
    private readonly(ushort)*? data;
    private nuint length;

    public Utf16Scalars() nothrow { data = null; length = 0; }
    public Utf16Scalars(readonly(ushort)*? text, nuint count)
    {
        if (text == null && count != 0) throw new Exception("UTF-16 storage is null");
        data = text; length = count;
    }

    public nuint CodeUnitLength { readonly get => length; }
    public readonly Utf16Scalars::Enumerator GetEnumerator()
        => new Utf16Scalars::Enumerator(data, length);

    public struct Enumerator : IEnumerator<uint>
    {
        private readonly(ushort)*? data;
        private nuint length;
        private nuint offset;
        private uint current;
        private bool valid;

        public Enumerator(readonly(ushort)*? text, nuint count)
        {
            if (text == null && count != 0) throw new Exception("UTF-16 storage is null");
            data = text; length = count; offset = 0; current = 0u; valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (offset == length) return false;
            uint scalar = 0u; nuint consumed = 0;
            if (!Utf16::TryDecode(data! + offset, length - offset, &scalar, &consumed))
                throw new Exception("Invalid or truncated UTF-16");
            offset += consumed; current = scalar; valid = true;
            return true;
        }

        public uint Current
        {
            readonly get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }
}
