using Standard::Collections;

namespace Standard::Queries;

public Enumerable<R, GroupJoinSource<T, U, K, R, OS, OE, IS, IE>, GroupJoinSource<T, U, K, R, OS, OE, IS, IE>::Enumerator>
    GroupJoin<T, U, K : IHashable, R, OS, OE, IS, IE>(Enumerable<T, OS, OE> outer, Enumerable<U, IS, IE> inner,
        K(T)* outerKey, K(U)* innerKey, R(T, Group<U>)* resultSelector)
    => outer.GroupJoin<U, K, R, IS, IE>(inner, outerKey, innerKey, resultSelector);

extension Enumerable<T, OS, OE>
{
    public Enumerable<R, GroupJoinSource<T, U, K, R, OS, OE, IS, IE>, GroupJoinSource<T, U, K, R, OS, OE, IS, IE>::Enumerator>
        GroupJoin<U, K : IHashable, R, IS, IE>(Enumerable<U, IS, IE> inner,
            K(T)* outerKey, K(U)* innerKey, R(T, Group<U>)* resultSelector)
        => new Enumerable<R, GroupJoinSource<T, U, K, R, OS, OE, IS, IE>, GroupJoinSource<T, U, K, R, OS, OE, IS, IE>::Enumerator>(
            new GroupJoinSource<T, U, K, R, OS, OE, IS, IE>((*this).SourceView(), inner.SourceView(), outerKey, innerKey, resultSelector));
}
