using std::Collections;
namespace std::Queries;

// Materializes ordinary value copies; duplicate arguments are destroyed by Add.
public HashSet<T> ToHashSet<T : IHashable, S, E>(this Enumerable<T, S, E> source)
{
    HashSet<T> result = new HashSet<T>();
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) result.Add(iterator.Current);
    return move result;
}
