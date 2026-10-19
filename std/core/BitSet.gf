namespace std;

// Owns a resizable sequence of packed bits. New bits start clear.
struct BitSet : IEnumerable<nuint, BitSet::Enumerator>
{
    private uint*? data;
    private nuint length;
    private nuint words;

    public BitSet() { data = null; length = 0; words = 0; }
    public BitSet(nuint size) { data = null; length = 0; words = 0; this.Resize(size); }
    public nuint Length { readonly get => length; }

    public bool this[nuint index]
    {
        readonly get
        {
            if (index >= length) throw new Exception("BitSet index out of range");
            uint mask = (uint)1 << (int)(index % 32);
            return (data![index / 32] & mask) != (uint)0;
        }
        set
        {
            if (index >= length) throw new Exception("BitSet index out of range");
            uint mask = (uint)1 << (int)(index % 32);
            if (value) data![index / 32] = data![index / 32] | mask;
            else data![index / 32] = data![index / 32] & (mask ^ (uint)4294967295);
        }
    }

    public void Resize(nuint size)
    {
        nuint requested = size / 32;
        if (size % 32 != 0) requested++;
        if (requested != words)
        {
            uint*? replacement = null;
            if (requested != 0)
            {
                void*? allocation = Memory::Allocate(requested * (nuint)sizeof(uint));
                if (allocation == null) throw new BadAllocException("BitSet allocation failed");
                replacement = (uint*)allocation;
                for (nuint i = 0; i < requested; i++)
                {
                    if (i < words) replacement![i] = data![i];
                    else replacement![i] = (uint)0;
                }
            }
            if (data != null) Memory::Free((void*)data);
            data = replacement;
            words = requested;
        }
        length = size;
        this.MaskUnusedBits();
    }

    private void MaskUnusedBits()
    {
        if (length % 32 != 0)
            data![words - 1] = data![words - 1] & (((uint)1 << (int)(length % 32)) - (uint)1);
    }

    public void SetAll(bool value)
    {
        uint bits = (uint)0;
        if (value) bits = (uint)4294967295;
        for (nuint i = 0; i < words; i++) data![i] = bits;
        this.MaskUnusedBits();
    }

    public void Clear() { this.SetAll(false); }

    public void Flip(nuint index)
    {
        if (index >= length) throw new Exception("BitSet index out of range");
        data![index / 32] = data![index / 32] ^ ((uint)1 << (int)(index % 32));
    }

    public readonly nuint CountSetBits()
    {
        nuint total = 0;
        for (nuint i = 0; i < words; i++)
        {
            uint bits = data![i];
            while (bits != (uint)0) { bits = bits & (bits - (uint)1); total++; }
        }
        return total;
    }

    public readonly BitSet Clone()
    {
        BitSet result = new BitSet(length);
        for (nuint i = 0; i < words; i++) result.data![i] = data![i];
        return result;
    }

    public void UnionWith(readonly(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] | other.data![i];
    }

    public void IntersectWith(readonly(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] & other.data![i];
    }

    public void ExceptWith(readonly(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] & (other.data![i] ^ (uint)4294967295);
    }

    public void SymmetricExceptWith(readonly(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] ^ other.data![i];
    }

    // Enumerates the indices of set bits in ascending order. Borrows storage.
    public readonly Enumerator GetEnumerator() => new Enumerator(data, length);

    ~BitSet() { if (data != null) Memory::Free((void*)data); }

    public struct Enumerator
    {
        private readonly(uint)*? data;
        private nuint length;
        private nuint position;
        private nuint current;
        private bool valid;
        public Enumerator(readonly(uint)*? storage, nuint size)
        {
            if (storage == null && size != 0) throw new Exception("BitSet enumerator storage is null");
            data = storage; length = size; position = 0; current = 0; valid = false;
        }
        public bool MoveNext()
        {
            valid = false;
            while (position < length)
            {
                nuint index = position;
                position++;
                if ((data![index / 32] & ((uint)1 << (int)(index % 32))) != (uint)0)
                {
                    current = index; valid = true; return true;
                }
            }
            return false;
        }
        public nuint Current
        {
            readonly get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }
}

