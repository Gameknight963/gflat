namespace std;

// Bridges a type's comparison contract to APIs accepting borrowed callbacks.
static class Comparer<T : IComparable<T>>
{
    public static int Compare(readonly(T)* left, readonly(T)* right)
        => left.CompareTo(right);
}
