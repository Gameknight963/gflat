namespace Standard::Text;

// Forwards writes to two borrowed writers, first then second. Keep both endpoints alive.
public struct TeeWriter : ITextWriter
{
    private ITextWriter* first;
    private ITextWriter* second;

    public TeeWriter(ITextWriter* first, ITextWriter* second) nothrow
    {
        this.first = first;
        this.second = second;
    }

    // Forwards the bytes, including zero-count calls. An exception stops before later forwarding.
    // Input must be independent of storage either endpoint can mutate.
    public void Write(readonly(char)* data, nuint count)
    {
        first.Write(data, count);
        second.Write(data, count);
    }
}
