using std::Collections;
namespace std;

public bool SequenceEqual<T, LS, LE, RS, RE>(this Enumerable<T, LS, LE> left,
    Enumerable<T, RS, RE> right, bool(immutable(T)*, immutable(T)*)* equal)
{
    LE first = left.GetEnumerator();
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

public bool SequenceEqual<T : IEquatable<T>, LS, LE, RS, RE>(this Enumerable<T, LS, LE> left,
    Enumerable<T, RS, RE> right)
    => std::SequenceEqual<T, LS, LE, RS, RE>(move left, move right, EqualityComparer<T>::Equals);
