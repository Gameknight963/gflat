using Standard::Memory;

namespace Standard::Queries;

// Append a key level without invoking earlier selectors again.
struct ThenOrderKey<T, Previous, K>
{
    private Previous previous;
    private OrderKey<T, K> next;

    public ThenOrderKey(Previous prior, K(T)* selector,
        int(immutable(K)*, immutable(K)*)* compare, bool descending)
    {
        previous = move prior;
        next = new OrderKey<T, K>(selector, compare, descending);
    }

    public Keys Build(Span<immutable(T)> values)
    {
        Previous::Keys prior = previous.Build(values);
        OrderKey<T, K>::Keys following = next.Build(values);
        return new Keys(move prior, move following);
    }

    public struct Keys
    {
        private Previous::Keys previous;
        private OrderKey<T, K>::Keys next;

        public Keys(Previous::Keys prior, OrderKey<T, K>::Keys following)
        {
            previous = move prior;
            next = move following;
        }

        public immutable int Compare(nuint left, nuint right)
        {
            int relation = previous.Compare(left, right);
            if (relation != 0) return relation;
            return next.Compare(left, right);
        }
    }
}
