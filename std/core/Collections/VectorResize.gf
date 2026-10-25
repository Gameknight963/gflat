namespace std;

// Copying resize stays outside Vector<T>, like AddRange.
public void Resize<T>(this ref Vector<T> target, nuint count, T fill)
{
    if (count <= target.Count)
    {
        target.Truncate(count);
        return;
    }
    while (target.Count < count) target.Add(fill);
}
