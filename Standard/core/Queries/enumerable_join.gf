using Standard::Collections;

namespace Standard::Queries;

public Enumerable<R, JoinSource<T, U, K, R, OS, OE, IS, IE>, JoinSource<T, U, K, R, OS, OE, IS, IE>::Enumerator>
    Join<T, U, K : IHashable, R, OS, OE, IS, IE>(Enumerable<T, OS, OE> outer, Enumerable<U, IS, IE> inner,
        K(T)* outerKey, K(U)* innerKey, R(T, U)* resultSelector)
    => outer.Join<U, K, R, IS, IE>(inner, outerKey, innerKey, resultSelector);

extension Enumerable<T, OS, OE>
{
    public Enumerable<R, JoinSource<T, U, K, R, OS, OE, IS, IE>, JoinSource<T, U, K, R, OS, OE, IS, IE>::Enumerator>
        Join<U, K : IHashable, R, IS, IE>(Enumerable<U, IS, IE> inner,
            K(T)* outerKey, K(U)* innerKey, R(T, U)* resultSelector)
        => new Enumerable<R, JoinSource<T, U, K, R, OS, OE, IS, IE>, JoinSource<T, U, K, R, OS, OE, IS, IE>::Enumerator>(
            new JoinSource<T, U, K, R, OS, OE, IS, IE>((*this).SourceView(), inner.SourceView(), outerKey, innerKey, resultSelector));
}
