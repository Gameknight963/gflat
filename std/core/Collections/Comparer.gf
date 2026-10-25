namespace std::Collections;

// Bridges a type's comparison contract to APIs accepting borrowed callbacks.
static class Comparer<T : IComparable<T>>
{
    public static int Compare(immutable(T)* left, immutable(T)* right)
        => left.CompareTo(right);
}
