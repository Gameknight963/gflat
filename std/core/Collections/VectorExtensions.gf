namespace std;

// Kept outside Vector<T>: only calls to this copying operation instantiate its
// copy requirement. A vector of noncopyable values can still use its own methods.
public void AddRange<T>(this ref Vector<T> target, Span<readonly(T)> source)
{
    nuint length = source.Length;
    if (length == 0) return;
    nuint originalCount = target.Count;
    nuint maximum = (nuint)0 - (nuint)1;
    if (length > maximum - originalCount) throw new Exception("Vector count overflow");

    // The source may borrow this vector. Save its element offset before Reserve
    // relocates and frees the old buffer; rebind that view after growth.
    bool aliasesTarget = false;
    nuint offset = 0;
    if (originalCount != 0)
    {
        Span<readonly(T)> original = target.AsReadOnly();
        nuint beginning = (nuint)original.Data!;
        nuint address = (nuint)source.Data!;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (address >= beginning)
        {
            nuint difference = address - beginning;
            offset = difference / stride;
            if (difference % stride == 0 && offset < originalCount)
            {
                if (length > originalCount - offset) throw new Exception("Vector source range is out of bounds");
                aliasesTarget = true;
            }
        }
    }
    target.Reserve(originalCount + length);
    if (aliasesTarget) source = target.AsReadOnly().Slice(offset, length);
    for (nuint i = 0; i < length; i++) target.Add(source[i]);
}
