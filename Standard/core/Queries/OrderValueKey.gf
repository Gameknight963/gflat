using Standard::Memory;

namespace Standard::Queries;

// Compare buffered values directly instead of constructing identity-key copies.
struct OrderValueKey<T>
{
    private int(immutable(T)*, immutable(T)*)* compare;
    private bool descending;

    public OrderValueKey(int(immutable(T)*, immutable(T)*)* comparison, bool reverse)
    {
        compare = comparison;
        descending = reverse;
    }

    public Keys Build(Span<immutable(T)> values) => new Keys(values, compare, descending);

    public struct Keys
    {
        private Span<immutable(T)> values;
        private int(immutable(T)*, immutable(T)*)* compare;
        private bool descending;

        public Keys(Span<immutable(T)> input, int(immutable(T)*, immutable(T)*)* comparison, bool reverse)
        {
            values = input;
            compare = comparison;
            descending = reverse;
        }

        public immutable int Compare(nuint left, nuint right)
        {
            if (descending) return compare(&values[right], &values[left]);
            return compare(&values[left], &values[right]);
        }
    }
}
