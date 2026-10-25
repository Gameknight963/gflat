namespace std::Queries;

public Enumerable<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>
    Concat<T, LS, LE, RS, RE>(this Enumerable<T, LS, LE> first, Enumerable<T, RS, RE> second)
    => new Enumerable<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>(
        new ConcatSource<T, LS, LE, RS, RE>(first.SourceView(), second.SourceView()));
