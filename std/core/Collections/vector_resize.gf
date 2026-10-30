namespace std::Collections;

/// Changes the initialized length, default-constructing new elements without copying.
/// Constructor failure restores Count and the old contents; capacity may have grown and borrows may be invalid.
public void Resize<T>(ref Vector<T> target, nuint count)
{
    if (count <= target.Count) { target.Truncate(count); return; }
    nuint originalCount = target.Count;
    target.Reserve(count);
    try { while (target.Count < count) target.Add(new T()); }
    catch { target.Truncate(originalCount); throw; }
}

extension<T> Vector<T>
{
    public void Resize(nuint count)
    => std::Collections::Resize<T>(ref *this, count);
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

extension<T> Vector<T>
{
    public void Resize(nuint count, T fill)
    => std::Collections::Resize<T>(ref *this, count, fill);
}
