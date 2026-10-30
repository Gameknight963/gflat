using std::Collections;
namespace std::Queries;

// Copies values into a new owning vector. Pointer elements remain pointers:
// neither this operation nor Vector deletes their pointees.
public Vector<T> ToVector<T, S, E>(Enumerable<T, S, E> source)
=> source.ToVector();

extension<T, S, E> Enumerable<T, S, E>
{
    public Vector<T> ToVector()
    {
        Vector<T> result = new Vector<T>();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext()) result.Add(iterator.Current);
        return move result;
    }
}

