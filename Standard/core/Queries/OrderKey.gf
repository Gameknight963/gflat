using Standard::Collections;
using Standard::Memory;

namespace Standard::Queries;

// A copyable description. Each cursor builds its own owning key cache.
struct OrderKey<T, K>
{
    private K(T)* selector;
    private int(immutable(K)*, immutable(K)*)* compare;
    private bool descending;

    public OrderKey(K(T)* keySelector, int(immutable(K)*, immutable(K)*)* comparison, bool reverse)
    {
        selector = keySelector;
        compare = comparison;
        descending = reverse;
    }

    public Keys Build(Span<immutable(T)> values) => new Keys(values, selector, compare, descending);

    public struct Keys
    {
        private Vector<K> values;
        private int(immutable(K)*, immutable(K)*)* compare;
        private bool descending;

        public Keys(Span<immutable(T)> input, K(T)* selector,
            int(immutable(K)*, immutable(K)*)* comparison, bool reverse)
        {
            values = new();
            compare = comparison;
            descending = reverse;
            values.Reserve(input.Length);
            for (nuint i = 0; i < input.Length; i++) values.Add(selector(input[i]));
        }

        public immutable int Compare(nuint left, nuint right)
        {
            if (descending) return compare(&values[right], &values[left]);
            return compare(&values[left], &values[right]);
        }
    }
}
