namespace Standard::Collections;

// Changes Count, default-constructing new elements when growing.
// Constructor failure restores the old Count and contents. Capacity growth can still invalidate borrows.
public void Resize<T>(ref Vector<T> target, nuint count)
{
    if (count <= target.Count) { target.Truncate(count); return; }
    nuint originalCount = target.Count;
    target.Reserve(count);
    try { while (target.Count < count) target.Add(new T()); }
    catch { target.Truncate(originalCount); throw; }
}

extension Vector<T>
{
    public void Resize(nuint count)
    => Standard::Collections::Resize<T>(ref *this, count);
}

// Copying resize stays outside Vector<T>, like AddRange.
public void Resize<T>(ref Vector<T> target, nuint count, T fill)
{
    if (count <= target.Count)
    {
        target.Truncate(count);
        return;
    }
    while (target.Count < count) target.Add(fill);
}

extension Vector<T>
{
    public void Resize(nuint count, T fill)
    => Standard::Collections::Resize<T>(ref *this, count, move fill);
}
