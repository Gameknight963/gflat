namespace std;

// Borrows caller storage. Capacity is payload bytes; constructor size includes
// the trailing null. No allocation, ownership transfer, or libc dependency.
public struct BufferWriter : ITextWriter
{
    private char* data;
    private nuint storageCapacity;
    private nuint length;

    public BufferWriter(char* storage, nuint capacity)
    {
        data = storage;
        storageCapacity = capacity;
        length = 0;
        if (storageCapacity != 0) data[0] = '\0';
    }

    public nuint Length { readonly get => length; }
    public nuint Capacity
    {
        readonly get
        {
            if (storageCapacity == 0) return 0;
            return storageCapacity - 1;
        }
    }
    public nuint Remaining { readonly get => this.Capacity - length; }
    public readonly(char)* Data { readonly get => data; }

    public void Clear()
    {
        length = 0;
        if (storageCapacity != 0) data[0] = '\0';
    }

    // Each call either appends every byte or leaves the writer unchanged.
    // Source may overlap the buffer, including the current trailing terminator.
    public void Write(readonly(char)* text, nuint count)
    {
        if (count > this.Remaining) throw new BufferTooSmallException();
        if (count == 0) return;
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
    }

    public void Write(readonly(char)* text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.Write(text, count);
    }
}
