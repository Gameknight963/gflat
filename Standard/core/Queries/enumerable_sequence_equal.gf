using Standard::Collections;
namespace Standard::Queries;

public bool SequenceEqual<T, LS, LE, RS, RE>(Enumerable<T, LS, LE> left,
    Enumerable<T, RS, RE> right, bool(immutable(T)*, immutable(T)*)* equal)
=> left.SequenceEqual<RS, RE>(right, equal);

extension<T, LS, LE> Enumerable<T, LS, LE>
{
    public bool SequenceEqual<RS, RE>(Enumerable<T, RS, RE> right, bool(immutable(T)*, immutable(T)*)* equal)
    {
        LE first = (*this).GetEnumerator();
        RE second = right.GetEnumerator();
        while (true)
        {
            bool hasFirst = first.MoveNext();
            bool hasSecond = second.MoveNext();
            if (hasFirst != hasSecond) return false;
            if (!hasFirst) return true;
            T firstValue = first.Current;
            T secondValue = second.Current;
            if (!equal(&firstValue, &secondValue)) return false;
        }
    }
}

public bool SequenceEqual<T : IEquatable<T>, LS, LE, RS, RE>(Enumerable<T, LS, LE> left,
    Enumerable<T, RS, RE> right)
    => left.SequenceEqual<RS, RE>(right);

extension<T : IEquatable<T>, LS, LE> Enumerable<T, LS, LE>
{
    public bool SequenceEqual<RS, RE>(Enumerable<T, RS, RE> right)
    => this.SequenceEqual<RS, RE>( move right, EqualityComparer<T>::Equals);
}
