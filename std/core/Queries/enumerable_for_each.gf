using std::Collections;
namespace std::Queries;

public void ForEach<T, S, E>(this Enumerable<T, S, E> source, void(T)* action)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) action(iterator.Current);
}

