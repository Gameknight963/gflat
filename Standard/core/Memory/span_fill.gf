namespace Standard::Memory;

// Assign into initialized storage. The value parameter snapshots aliased input.
// Keep copying lazy: Span<Noncopyable> remains usable without calling Fill.
public void Fill<T>(Span<T> destination, T value)
=> destination.Fill(value);

extension<T> Span<T>
{
    public readonly void Fill(T value)
    {
        T*? storage = this.Data;
        for (nuint i = 0; i < this.Length; i++) storage![i] = value;
    }
}
