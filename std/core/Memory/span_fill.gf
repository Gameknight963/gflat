namespace std::Memory;

// Assign into initialized storage. The value parameter snapshots aliased input.
// Keep copying lazy: Span<Noncopyable> remains usable without calling Fill.
public void Fill<T>(Span<T> destination, T value)
=> destination.Fill(value);

extension<T> Span<T>
{
    public void Fill(T value)
    {
        for (nuint i = 0; i < (*this).Length; i++) (*this)[i] = value;
    }
}
