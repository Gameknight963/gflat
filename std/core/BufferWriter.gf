namespace std;

// Borrows caller storage. Capacity is payload bytes; constructor size includes
// the trailing null. No allocation, ownership transfer, or libc dependency.
public struct BufferWriter : ITextWriter
{
    private char* data;
    private nuint storageCapacity;
    private nuint length;

    public BufferWriter(char* storage, nuint capacity) nothrow
    {
        data = storage;
        storageCapacity = capacity;
        length = 0;
        if (storageCapacity != 0) data[0] = '\0';
    }

    public nuint Length { readonly get nothrow => length; }
    public nuint Capacity
    {
        readonly get nothrow
        {
            if (storageCapacity == 0) return 0;
            return storageCapacity - 1;
        }
    }
    public nuint Remaining { readonly get nothrow => this.Capacity - length; }
    public readonly(char)* Data { readonly get nothrow => data; }

    public void Clear() nothrow
    {
        length = 0;
        if (storageCapacity != 0) data[0] = '\0';
    }

    // Each call either appends every byte or leaves the writer unchanged.
    // Source may overlap the buffer, including the current trailing terminator.
    public bool TryWrite(readonly(char)* text, nuint count) nothrow
    {
        if (count > this.Remaining) return false;
        if (count == 0) return true;
        char* target = data + length;
        if ((readonly(char)*)target > text && (readonly(char)*)target < text + count)
        {
            nuint i = count;
            while (i != 0) { i--; target[i] = text[i]; }
        }
        else
            for (nuint i = 0; i < count; i++) target[i] = text[i];
        length += count;
        data[length] = '\0';
        return true;
    }

    public bool TryWrite(readonly(char)* text) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return this.TryWrite(text, count);
    }

    public void Write(readonly(char)* text, nuint count)
    {
        if (!this.TryWrite(text, count)) throw new BufferTooSmallException();
    }

    public void Write(readonly(char)* text)
    {
        if (!this.TryWrite(text)) throw new BufferTooSmallException();
    }
}
