namespace std;

// Equality uses the type's declared contract, including primitive implementations.
static class EqualityComparer<T : IEquatable<T>>
{
    public static bool Equals(readonly(T)* left, readonly(T)* right)
        => left.Equals(right);
}
