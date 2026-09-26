namespace std;

// Borrows storage; the caller keeps the buffer alive for every use of this view.
struct Span<T>
{
    private T*? data;
    private nuint length;

    public Span() { data = null; length = 0; }
    public Span(T* buffer, nuint count) { data = buffer; length = count; }

    public nuint Length { readonly get => length; }
    public bool IsEmpty { readonly get => length == (nuint)0; }
    public T*? Data { get => data; }

    public T this[nuint index]
    {
        get throws
        {
            if (index >= length) throw new* Exception(c"Span index out of range", 3);
            return ((T*)data)[index];
        }
        set throws
        {
            if (index >= length) throw new* Exception(c"Span index out of range", 3);
            ((T*)data)[index] = value;
        }
    }

    public Span<T> Slice(nuint start, nuint count) throws
    {
        if (start > length || count > length - start)
            throw new* Exception(c"Span slice out of range", 3);
        if (count == (nuint)0) return new Span<T>();
        return new Span<T>((T*)data + start, count);
    }

    public Span<T> Slice(nuint start) throws
    {
        if (start > length) throw new* Exception(c"Span slice out of range", 3);
        return this.Slice(start, length - start);
    }

    public readonly ReadOnlySpan<T> AsReadOnly()
    {
        if (length == (nuint)0) return new ReadOnlySpan<T>();
        return new ReadOnlySpan<T>((readonly(T)*)data, length);
    }
}
