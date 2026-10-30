using std::Collections;
namespace std::Queries;

public void ForEach<T, S, E>(Enumerable<T, S, E> source, void(T)* action)
=> source.ForEach(action);

extension<T, S, E> Enumerable<T, S, E>
{
    public void ForEach(void(T)* action)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext()) action(iterator.Current);
    }
}

