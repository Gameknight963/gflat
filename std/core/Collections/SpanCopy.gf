namespace std;

// Assignment into initialized elements, not construction into raw storage.
// Kept separate so merely using Span<T> never requires T to support copying.
public void CopyTo<T>(this Span<immutable(T)> source, Span<T> destination)
{
    nuint count = source.Length;
    if (destination.Length < count) throw new Exception("Span copy destination is too short");
    if (count == 0) return;
    immutable(T)* input = source.Data!;
    T* output = destination.Data!;
    if ((nuint)input == (nuint)output) return;
    // Backward assignment preserves unread source elements when the destination
    // starts inside the source. It is also valid for disjoint later storage.
    if ((nuint)output > (nuint)input)
    {
        while (count != 0) { count--; output[count] = input[count]; }
    }
    else
    {
        for (nuint i = 0; i < count; i++) output[i] = input[i];
    }
}

// False means insufficient destination length; copying errors still propagate.
public bool TryCopyTo<T>(this Span<immutable(T)> source, Span<T> destination)
{
    if (destination.Length < source.Length) return false;
    std::CopyTo<T>(source, destination);
    return true;
}
