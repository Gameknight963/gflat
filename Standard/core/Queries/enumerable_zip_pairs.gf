namespace Standard::Queries;

private static class ZipPairFactory<T, U>
{
    public static Zipped<T, U> Create(T first, U second)
        => new Zipped<T, U>(move first, move second);
}

public Enumerable<Zipped<T, U>, ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>,
    ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>::Enumerator>
    Zip<T, U, LS, LE, RS, RE>(Enumerable<T, LS, LE> left, Enumerable<U, RS, RE> right)
    => left.Zip<U, RS, RE>(right);

extension Enumerable<T, LS, LE>
{
    public Enumerable<Zipped<T, U>, ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>,
        ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>::Enumerator>
        Zip<U, RS, RE>(Enumerable<U, RS, RE> right)
        => new Enumerable<Zipped<T, U>, ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>,
            ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>::Enumerator>(
                new ZipSource<T, U, Zipped<T, U>, LS, LE, RS, RE>(
                    (*this).SourceView(), right.SourceView(), ZipPairFactory<T, U>::Create));
}
