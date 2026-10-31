namespace Standard::Text;

/// Forwards each counted write to two borrowed writers, first then second.
/// Copies share the endpoints. Keep both alive and valid during every call.
public struct TeeWriter : ITextWriter
{
    private ITextWriter* first;
    private ITextWriter* second;

    public TeeWriter(ITextWriter* first, ITextWriter* second) nothrow
    {
        this.first = first;
        this.second = second;
    }

    /// Forwards zero counts too. An exception stops forwarding without rollback.
    /// Input must stay stable and disjoint from storage either endpoint may mutate.
    public void Write(readonly(char)* data, nuint count)
    {
        first.Write(data, count);
        second.Write(data, count);
    }
}
