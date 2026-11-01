using Standard::Collections;
using Standard::Memory;

namespace Standard::IO;

// Owns counted bytes and one cursor. Views borrow the current buffer.
public struct MemoryStream : IByteReader, ITextWriter
{
    private Vector<char> buffer;
    private long position;
    private static const long MaximumPosition = 9223372036854775807l;

    public MemoryStream() nothrow { buffer = new Vector<char>(); position = 0l; }

    public MemoryStream(nuint capacity)
    {
        buffer = new Vector<char>();
        position = 0l;
        this.Reserve(capacity);
    }

    public copy MemoryStream(immutable(MemoryStream)* source)
    {
        buffer = new Vector<char>();
        position = source.position;
        buffer.Reserve(source.buffer.Count);
        for (nuint index = 0; index < source.buffer.Count; index++) buffer.Add(source.buffer[index]);
    }

    public long Length { immutable get nothrow => (long)buffer.Count; }
    public nuint Capacity { immutable get nothrow => buffer.Capacity; }
    public long Position
    {
        immutable get nothrow => position;
        set
        {
            if (value < 0l) throw new IOException("Memory stream position must be nonnegative");
            position = value;
        }
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        long start = 0l;
        if (origin == SeekOrigin::Current) start = position;
        else if (origin == SeekOrigin::End) start = this.Length;
        else if (origin != SeekOrigin::Begin) throw new IOException("Invalid seek origin");
        if (offset < -start || (offset > 0l && offset > MaximumPosition - start))
            throw new IOException("Memory stream seek is outside the supported range");
        position = start + offset;
        return position;
    }

    public void Reserve(nuint capacity)
    {
        if ((ulong)capacity > (ulong)MaximumPosition)
            throw new IOException("Memory stream capacity exceeds the supported range");
        buffer.Reserve(capacity);
    }

    // Extends with zero bytes, preserving Position even beyond the new end.
    public void SetLength(long length)
    {
        nuint count = this.StorageCount(length);
        this.EnsureCapacity(count);
        buffer.Resize(count);
    }

    // Retains capacity and resets Length and Position to zero.
    public void Clear()
    {
        buffer.Clear();
        position = 0l;
    }

    public void Flush() nothrow { }

    // Growth can relocate storage. Length changes invalidate removed ranges.
    public Span<char> AsSpan() => buffer.AsSpan();
    public readonly Span<readonly(char)> AsReadOnly() => buffer.AsReadOnly();

    public nuint Read(char* data, nuint count)
    {
        if (count == 0 || position >= this.Length) return 0;
        nuint start = (nuint)position;
        nuint take = buffer.Count - start;
        if (take > count) take = count;
        Span<char> source = buffer.AsSpan();
        for (nuint index = 0; index < take; index++) data[index] = source.Data![start + index];
        position += (long)take;
        return take;
    }

    public int ReadByte()
    {
        int value = this.PeekByte();
        if (value != -1) position++;
        return value;
    }

    public readonly int PeekByte()
    {
        if (position >= this.Length) return -1;
        return (int)(byte)buffer[(nuint)position];
    }

    // Input may borrow the current contents, including overlapping destination bytes.
    // Allocation failure leaves contents, Length, and Position unchanged.
    public void Write(readonly(char)* data, nuint count)
    {
        if (count == 0) return;
        if ((ulong)count > (ulong)(MaximumPosition - position))
            throw new IOException("Memory stream write exceeds the supported range");
        long end = position + (long)count;
        nuint required = this.StorageCount(end);
        bool selfSource = false;
        nuint offset = 0;
        if (buffer.Count != 0)
        {
            Span<char> old = buffer.AsSpan();
            nuint address = (old.Data!).reinterpret<nuint>();
            nuint source = data.reinterpret<nuint>();
            if (source >= address && source - address < buffer.Capacity)
            {
                offset = source - address;
                if (offset > buffer.Count || count > buffer.Count - offset)
                    throw new IOException("Memory stream source is outside its current contents");
                selfSource = true;
            }
        }
        this.EnsureCapacity(required);
        if (required > buffer.Count) buffer.Resize(required);
        Span<char> storage = buffer.AsSpan();
        char* destination = storage.Data! + (nuint)position;
        if (selfSource) data = storage.Data! + offset;
        if (selfSource && (nuint)position > offset && (nuint)position - offset < count)
        {
            nuint remaining = count;
            while (remaining != 0) { remaining--; destination[remaining] = data[remaining]; }
        }
        else
            for (nuint index = 0; index < count; index++) destination[index] = data[index];
        position = end;
    }

    public void WriteByte(char value) { this.Write(&value, 1); }

    private readonly nuint StorageCount(long length)
    {
        if (length < 0l || (ulong)length > (ulong)((nuint)0 - (nuint)1))
            throw new IOException("Memory stream length exceeds the supported range");
        return (nuint)length;
    }

    private void EnsureCapacity(nuint required)
    {
        if (required <= buffer.Capacity) return;
        nuint maximum = (nuint)0 - (nuint)1;
        if ((ulong)maximum > (ulong)MaximumPosition) maximum = (nuint)MaximumPosition;
        nuint grown = 256;
        if (buffer.Capacity != 0)
        {
            grown = maximum;
            if (buffer.Capacity <= maximum / 2) grown = buffer.Capacity * 2;
        }
        if (grown < required) grown = required;
        if (grown > maximum) grown = maximum;
        buffer.Reserve(grown);
    }
}
