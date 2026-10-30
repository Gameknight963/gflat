using std::Memory;
namespace std::Collections;

// Optional copying operation: merely including it does not require copyable T.
public void InsertRange<T>(ref Vector<T> target, nuint index, Span<immutable(T)> source)
{
    nuint originalCount = target.Count;
    if (index > originalCount) throw new Exception("Vector insertion index out of range");
    if (source.Length == 0) return;
    try
    {
        target.AddRange(source);
    }
    catch
    {
        target.Truncate(originalCount);
        throw;
    }
    // Copying has finished. Rotate only relocates the initialized elements.
    std::Collections::Rotate<T>(target.AsSpan().Slice(index), originalCount - index);
}

extension<T> Vector<T>
{
    public void InsertRange(nuint index, Span<immutable(T)> source)
    => std::Collections::InsertRange<T>(ref *this, index, source);
}
