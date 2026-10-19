namespace std;

public bool Any<T, S, E>(this Enumerable<T, S, E> source)
{
    E iterator = source.GetEnumerator();
    return iterator.MoveNext();
}

public bool All<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) if (!predicate(iterator.Current)) return false;
    return true;
}

public nuint Count<T, S, E>(this Enumerable<T, S, E> source)
{
    nuint count = 0;
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        if (count == (nuint)0 - (nuint)1) throw new Exception("Sequence count overflow");
        count++;
    }
    return count;
}

public T First<T, S, E>(this Enumerable<T, S, E> source)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
    return iterator.Current;
}

public T Last<T, S, E>(this Enumerable<T, S, E> source)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
    while (true)
    {
        T result = iterator.Current;
        if (!iterator.MoveNext()) return result;
    }
}

public T Single<T, S, E>(this Enumerable<T, S, E> source)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
    T result = iterator.Current;
    if (iterator.MoveNext()) throw new Exception("Sequence contains more than one element");
    return result;
}

// Leaves result unchanged when the sequence is empty.
public bool TryFirst<T, S, E>(this Enumerable<T, S, E> source, ref T result)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) return false;
    result = iterator.Current;
    return true;
}

// Do not replace result until every element and the final MoveNext succeeded.
public bool TryLast<T, S, E>(this Enumerable<T, S, E> source, ref T result)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) return false;
    while (true)
    {
        T last = iterator.Current;
        if (!iterator.MoveNext()) { result = move last; return true; }
    }
}

// Empty and multiple-element sequences both leave result unchanged.
public bool TrySingle<T, S, E>(this Enumerable<T, S, E> source, ref T result)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) return false;
    T single = iterator.Current;
    if (iterator.MoveNext()) return false;
    result = move single;
    return true;
}

public void ForEach<T, S, E>(this Enumerable<T, S, E> source, void(T)* action)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) action(iterator.Current);
}

// Copies values into a new owning vector. Pointer elements remain pointers:
// neither this operation nor Vector deletes their pointees.
public Vector<T> ToVector<T, S, E>(this Enumerable<T, S, E> source)
{
    Vector<T> result = new Vector<T>();
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) result.Add(iterator.Current);
    return move result;
}

public Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator> Select<T, U, S, E>(this Enumerable<T, S, E> source, U(T)* selector)
    => new Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator>(new SelectSource<T, U, S, E>(source.SourceView(), selector));

public U Aggregate<T, U, S, E>(this Enumerable<T, S, E> source, U seed, U(U, T)* accumulate)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) seed = accumulate(seed, iterator.Current);
    return move seed;
}

public Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator> Where<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
    => new Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator>(new WhereSource<T, S, E>(source.SourceView(), predicate));

public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Take<T, S, E>(this Enumerable<T, S, E> source, nuint count)
    => new Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator>(new SliceSource<T, S, E>(source.SourceView(), 0, count));

public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Skip<T, S, E>(this Enumerable<T, S, E> source, nuint count)
    => new Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator>(new SliceSource<T, S, E>(source.SourceView(), count, (nuint)0 - (nuint)1));

public Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator> TakeWhile<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
    => new Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator>(new TakeWhileSource<T, S, E>(source.SourceView(), predicate));

public Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator> SkipWhile<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
    => new Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator>(new SkipWhileSource<T, S, E>(source.SourceView(), predicate));
