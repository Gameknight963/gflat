namespace Standard::Queries;

public Enumerable<R, ZipSource<T, U, R, LS, LE, RS, RE>, ZipSource<T, U, R, LS, LE, RS, RE>::Enumerator>
    Zip<T, U, R, LS, LE, RS, RE>(Enumerable<T, LS, LE> left, Enumerable<U, RS, RE> right, R(T, U)* selector)
    => left.Zip<U, R, RS, RE>(right, selector);

extension<T, LS, LE> Enumerable<T, LS, LE>
{
    public Enumerable<R, ZipSource<T, U, R, LS, LE, RS, RE>, ZipSource<T, U, R, LS, LE, RS, RE>::Enumerator> Zip<U, R, RS, RE>(Enumerable<U, RS, RE> right, R(T, U)* selector)
    => new Enumerable<R, ZipSource<T, U, R, LS, LE, RS, RE>, ZipSource<T, U, R, LS, LE, RS, RE>::Enumerator>(
            new ZipSource<T, U, R, LS, LE, RS, RE>((*this).SourceView(), right.SourceView(), selector));
}
