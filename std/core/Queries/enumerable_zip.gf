namespace std::Queries;

public Enumerable<R, ZipSource<T, U, R, LS, LE, RS, RE>, ZipSource<T, U, R, LS, LE, RS, RE>::Enumerator>
    Zip<T, U, R, LS, LE, RS, RE>(this Enumerable<T, LS, LE> left, Enumerable<U, RS, RE> right, R(T, U)* selector)
    => new Enumerable<R, ZipSource<T, U, R, LS, LE, RS, RE>, ZipSource<T, U, R, LS, LE, RS, RE>::Enumerator>(
        new ZipSource<T, U, R, LS, LE, RS, RE>(left.SourceView(), right.SourceView(), selector));
