namespace std::Text;

// Counts bytes without reading or retaining the supplied data pointer.
public struct CountingWriter : ITextWriter
{
    private nuint length;

    public CountingWriter() nothrow { length = 0; }

    public nuint Length { immutable get nothrow => length; }

    public void Clear() nothrow { length = 0; }

    public bool TryWrite(readonly(char)* data, nuint count) nothrow
    {
        nuint maximum = (nuint)0 - (nuint)1;
        if (count > maximum - length) return false;
        length += count;
        return true;
    }

    public void Write(readonly(char)* data, nuint count)
    {
        if (!this.TryWrite(data, count)) throw new Exception("Writer length overflow");
    }
}
