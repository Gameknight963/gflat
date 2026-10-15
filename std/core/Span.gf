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
    public Span::Enumerator<T> GetEnumerator() => new Span::Enumerator<T>(data, length);
    public T*? Data { get => data; }

    public ref T this[nuint index]
    {
        get
        {
            if (index >= length) throw new Exception("Span index out of range", 3);
            return data![index];
        }
    }

    public Span<T> Slice(nuint start, nuint count)
    {
        if (start > length || count > length - start)
            throw new Exception("Span slice out of range", 3);
        if (count == (nuint)0) return new Span<T>();
        return new Span<T>((T*)data + start, count);
    }

    public Span<T> Slice(nuint start)
    {
        if (start > length) throw new Exception("Span slice out of range", 3);
        return this.Slice(start, length - start);
    }

    public readonly Span<readonly(T)> AsReadOnly()
    {
        if (length == (nuint)0) return new Span<readonly(T)>();
        return new Span<readonly(T)>((readonly(T)*)data, length);
    }

    // Borrows the same storage as its source view.
    public struct Enumerator<T>
    {
        private T*? data;
        private nuint length;
        private nuint position;
        private bool valid;

        public Enumerator(T*? storage, nuint count)
        {
            if (storage == null && count != (nuint)0) throw new Exception("Enumerator storage is null");
            data = storage; length = count; position = 0; valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (position >= length) return false;
            position++;
            valid = true;
            return true;
        }

        public ref T Current
        {
            get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return data![position - (nuint)1];
            }
        }
    }

}