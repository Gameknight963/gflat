namespace std;

// Assign into initialized storage. The value parameter snapshots aliased input.
// Keep copying lazy: Span<Noncopyable> remains usable without calling Fill.
public void Fill<T>(this Span<T> destination, T value)
{
    for (nuint i = 0; i < destination.Length; i++) destination[i] = value;
}
