namespace std::Collections;

// Owns a resizable sequence of packed bits. New bits start clear.
struct BitSet : IEnumerable<nuint, BitSet::Enumerator>, IEquatable<BitSet>, IHashable
{
    private uint*? data;
    private nuint length;
    private nuint words;

    public BitSet() { data = null; length = 0; words = 0; }
    public BitSet(nuint size) { data = null; length = 0; words = 0; this.Resize(size); }
    public copy BitSet(immutable(BitSet)* source)
    {
        data = null; length = 0; words = 0;
        this.Resize(source.length);
        for (nuint i = 0; i < words; i++) data![i] = source.data![i];
    }
    public nuint Length { immutable get nothrow => length; }

    public bool this[nuint index]
    {
        immutable get
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

    private void MaskUnusedBits() nothrow
    {
        if (length % 32 != 0)
            data![words - 1] = data![words - 1] & (((uint)1 << (int)(length % 32)) - (uint)1);
    }

    public void SetAll(bool value) nothrow
    {
        uint bits = (uint)0;
        if (value) bits = (uint)4294967295;
        for (nuint i = 0; i < words; i++) data![i] = bits;
        this.MaskUnusedBits();
    }

    public void Clear() nothrow { this.SetAll(false); }

    // Shifts keep Length, discard outgoing bits, and clear the vacated positions.
    public void ShiftLeft(nuint count) nothrow
    {
        if (count == 0) return;
        if (count >= length) { this.Clear(); return; }
        nuint whole = count / 32;
        int remainder = (int)(count % 32);
        // Read lower words before overwriting them.
        for (nuint end = words; end > whole; end--)
        {
            nuint target = end - 1;
            nuint source = target - whole;
            uint bits = data![source];
            if (remainder != 0)
            {
                bits = bits << remainder;
                if (source != 0) bits = bits | (data![source - 1] >> (32 - remainder));
            }
            data![target] = bits;
        }
        for (nuint i = 0; i < whole; i++) data![i] = 0;
        this.MaskUnusedBits();
    }

    public void ShiftRight(nuint count) nothrow
    {
        if (count == 0) return;
        if (count >= length) { this.Clear(); return; }
        nuint whole = count / 32;
        int remainder = (int)(count % 32);
        nuint remaining = words - whole;
        // Read higher words before overwriting them.
        for (nuint target = 0; target < remaining; target++)
        {
            nuint source = target + whole;
            uint bits = data![source];
            if (remainder != 0)
            {
                bits = bits >> remainder;
                if (source < words - 1) bits = bits | (data![source + 1] << (32 - remainder));
            }
            data![target] = bits;
        }
        for (nuint i = remaining; i < words; i++) data![i] = 0;
    }

    public void Flip(nuint index)
    {
        if (index >= length) throw new Exception("BitSet index out of range");
        data![index / 32] = data![index / 32] ^ ((uint)1 << (int)(index % 32));
    }

    // Validate before changing any word; an empty range may start at Length.
    public void SetRange(nuint start, nuint count, bool value)
    {
        if (start > length || count > length - start) throw new Exception("BitSet range out of bounds");
        nuint end = start + count;
        while (start < end)
        {
            nuint first = start % 32;
            nuint take = 32 - first;
            if (take > end - start) take = end - start;
            uint mask = BitSet::RangeMask(first, take);
            if (value) data![start / 32] = data![start / 32] | mask;
            else data![start / 32] = data![start / 32] & (mask ^ (uint)4294967295);
            start += take;
        }
    }

    public void FlipRange(nuint start, nuint count)
    {
        if (start > length || count > length - start) throw new Exception("BitSet range out of bounds");
        nuint end = start + count;
        while (start < end)
        {
            nuint first = start % 32;
            nuint take = 32 - first;
            if (take > end - start) take = end - start;
            data![start / 32] = data![start / 32] ^ BitSet::RangeMask(first, take);
            start += take;
        }
    }

    private static uint RangeMask(nuint first, nuint count) nothrow
    {
        // Shifting a uint by 32 is not a way to build a complete word mask.
        if (count == 32) return (uint)4294967295;
        return (((uint)1 << (int)count) - (uint)1) << (int)first;
    }

    // First matching logical bit at or after start; Length means no match.
    public immutable nuint FindIndex(bool value, nuint start = 0)
    {
        if (start > length) throw new Exception("BitSet search start out of range");
        if (start == length) return length;
        nuint wordIndex = start / 32;
        nuint offset = start % 32;
        while (wordIndex < words)
        {
            nuint beginning = wordIndex * 32;
            nuint available = length - beginning;
            if (available > 32) available = 32;
            uint bits = data![wordIndex];
            if (!value) bits = bits ^ 0xffffffffu;
            bits = bits & BitSet::RangeMask(offset, available - offset);
            if (bits != 0u)
            {
                nuint bit = 0;
                if ((bits & 0xffffu) == 0u) { bits = bits >> 16; bit += 16; }
                if ((bits & 0xffu) == 0u) { bits = bits >> 8; bit += 8; }
                if ((bits & 0xfu) == 0u) { bits = bits >> 4; bit += 4; }
                if ((bits & 3u) == 0u) { bits = bits >> 2; bit += 2; }
                if ((bits & 1u) == 0u) bit++;
                return beginning + bit;
            }
            wordIndex++;
            offset = 0;
        }
        return length;
    }

    public immutable nuint CountSetBits() nothrow
    {
        nuint total = 0;
        for (nuint i = 0; i < words; i++)
        {
            uint bits = data![i];
            while (bits != (uint)0) { bits = bits & (bits - (uint)1); total++; }
        }
        return total;
    }

    public immutable bool HasAnySet() nothrow
    {
        for (nuint i = 0; i < words; i++)
            if (data![i] != (uint)0) return true;
        return false;
    }

    // Fixed-universe intersection test; matching lengths follow other set operations.
    public immutable bool Overlaps(immutable(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++)
            if ((data![i] & other.data![i]) != (uint)0) return true;
        return false;
    }

    // Fixed-universe inclusion, with the same length rule as other set operations.
    public immutable bool IsSubsetOf(immutable(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++)
            if ((data![i] & other.data![i]) != data![i]) return false;
        return true;
    }

    public immutable bool IsSupersetOf(immutable(BitSet)* other)
        => other.IsSubsetOf(this);

    public immutable bool HasAllSet() nothrow
    {
        nuint complete = length / 32;
        for (nuint i = 0; i < complete; i++)
            if (data![i] != (uint)4294967295) return false;
        nuint remainder = length % 32;
        if (remainder != 0)
            return data![complete] == (((uint)1 << (int)remainder) - (uint)1);
        return true;
    }

    public immutable BitSet Clone()
    {
        BitSet result = *this;
        return move result;
    }

    public immutable bool Equals(immutable(BitSet)* other) nothrow
    {
        if (length != other.length) return false;
        for (nuint i = 0; i < words; i++)
            if (data![i] != other.data![i]) return false;
        return true;
    }

    public immutable nuint GetHashCode() nothrow
    {
        nuint hash = Hashing::Integer((ulong)length);
        for (nuint i = 0; i < words; i++)
            hash = Hashing::Combine(hash, Hashing::Integer((ulong)data![i]));
        return hash;
    }

    public void UnionWith(immutable(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] | other.data![i];
    }

    public void IntersectWith(immutable(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] & other.data![i];
    }

    public void ExceptWith(immutable(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] & (other.data![i] ^ (uint)4294967295);
    }

    public void SymmetricExceptWith(immutable(BitSet)* other)
    {
        if (length != other.length) throw new Exception("BitSet lengths differ");
        for (nuint i = 0; i < words; i++) data![i] = data![i] ^ other.data![i];
    }

    // Enumerates the indices of set bits in ascending order. Borrows storage.
    public immutable Enumerator GetEnumerator() => new Enumerator(data, length);

    public immutable Enumerable<nuint, BitSet::View, BitSet::Enumerator> AsEnumerable()
        => new Enumerable<nuint, BitSet::View, BitSet::Enumerator>(new View(data, length));

    ~BitSet() { if (data != null) Memory::Free((void*)data); }

    // Captures storage and bit length, not the owning BitSet or iterator position.
    public struct View : IEnumerable<nuint, BitSet::Enumerator>
    {
        private readonly(uint)*? data;
        private nuint length;
        public View(readonly(uint)*? storage, nuint size)
        {
            if (storage == null && size != 0) throw new Exception("BitSet view storage is null");
            data = storage; length = size;
        }
        public immutable BitSet::Enumerator GetEnumerator() => new BitSet::Enumerator(data, length);
    }

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
                nuint offset = position % 32;
                uint bits = data![position / 32] >> (int)offset;
                // Keep consecutive set bits cheap, and reload the word on every
                // call so edits ahead of this position remain observable.
                if ((bits & 1u) != 0u)
                {
                    current = position; position++; valid = true; return true;
                }
                nuint skip = 32 - offset;
                if (bits != 0u)
                {
                    skip = 0;
                    if ((bits & 0xffffu) == 0u) { bits = bits >> 16; skip += 16; }
                    if ((bits & 0xffu) == 0u) { bits = bits >> 8; skip += 8; }
                    if ((bits & 0xfu) == 0u) { bits = bits >> 4; skip += 4; }
                    if ((bits & 3u) == 0u) { bits = bits >> 2; skip += 2; }
                    if ((bits & 1u) == 0u) skip++;
                    // A public enumerator may borrow words with dirty padding.
                    if (skip >= length - position) { position = length; return false; }
                    current = position + skip; position = current + 1;
                    valid = true; return true;
                }
                if (skip >= length - position) { position = length; return false; }
                position += skip;
            }
            return false;
        }
        public nuint Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return current;
            }
        }
    }
}

