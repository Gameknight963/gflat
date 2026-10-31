using Standard::Queries;
using Standard::Collections;
namespace Standard::Memory;

/// Borrows contiguous storage.
/// Keep the backing buffer alive for every use of this view.
struct Span<T> : IEnumerable<T, Span<T>::Enumerator>
{
    private T*? data;
    private nuint length;

    public Span() { data = null; length = 0; }
    public Span(T* buffer, nuint count) { data = buffer; length = count; }

    public nuint Length { immutable get nothrow => length; }
    public bool IsEmpty { immutable get nothrow => length == 0; }
    public readonly Span<T>::Enumerator GetEnumerator() => new Span<T>::Enumerator(data, length);
    public readonly Enumerable<T, Span<T>, Span<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Span<T>, Span<T>::Enumerator>(*this);
    public T*? Data { readonly get nothrow => data; }

    public ref T this[nuint index]
    {
        immutable get
        {
            if (index >= length) throw new Exception("Span index out of range", 3);
            return data![index];
        }
    }

    public readonly Span<T> Slice(nuint start, nuint count)
    {
        if (start > length || count > length - start)
            throw new Exception("Span slice out of range", 3);
        if (count == 0) return new Span<T>();
        return new Span<T>((T*)data + start, count);
    }

    public readonly Span<T> Slice(nuint start)
    {
        if (start > length) throw new Exception("Span slice out of range", 3);
        return this.Slice(start, length - start);
    }

    public readonly Span<readonly(T)> AsReadOnly()
    {
        if (length == 0) return new Span<readonly(T)>();
        return new Span<readonly(T)>((readonly(T)*)data, length);
    }

    public immutable Span<immutable(T)> AsImmutable()
    {
        if (length == 0) return new Span<immutable(T)>();
        return new Span<immutable(T)>((immutable(T)*)data, length);
    }

    // Borrows the same storage as its source view.
    public struct Enumerator
    {
        private T*? data;
        private nuint length;
        private nuint position;
        private bool valid;

        public Enumerator(T*? storage, nuint count)
        {
            if (storage == null && count != 0) throw new Exception("Enumerator storage is null");
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
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return data![position - 1];
            }
        }
    }

}
