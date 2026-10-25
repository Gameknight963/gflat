using std::Collections;
namespace std::Queries;

// Copies values into a new owning vector. Pointer elements remain pointers:
// neither this operation nor Vector deletes their pointees.
public Vector<T> ToVector<T, S, E>(this Enumerable<T, S, E> source)
{
    Vector<T> result = new Vector<T>();
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) result.Add(iterator.Current);
    return move result;
}

