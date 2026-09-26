namespace std;

// Borrows storage; the caller keeps the buffer alive for every use of this view.
struct ReadOnlySpan<T>
{
    private readonly(T)*? data;
    private nuint length;

    public ReadOnlySpan() { data = null; length = 0; }
    public ReadOnlySpan(readonly(T)* buffer, nuint count) { data = buffer; length = count; }

    public nuint Length { readonly get => length; }
    public bool IsEmpty { readonly get => length == (nuint)0; }
    public readonly(T)*? Data { readonly get => data; }

    public readonly(T) this[nuint index]
    {
        readonly get throws
        {
            if (index >= length) throw new* Exception(c"Span index out of range", 3);
            return ((readonly(T)*)data)[index];
        }
    }

    public readonly ReadOnlySpan<T> Slice(nuint start, nuint count) throws
    {
        if (start > length || count > length - start)
            throw new* Exception(c"Span slice out of range", 3);
        if (count == (nuint)0) return new ReadOnlySpan<T>();
        return new ReadOnlySpan<T>((readonly(T)*)data + start, count);
    }

    public readonly ReadOnlySpan<T> Slice(nuint start) throws
    {
        if (start > length) throw new* Exception(c"Span slice out of range", 3);
        return this.Slice(start, length - start);
    }
}
