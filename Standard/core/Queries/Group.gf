using Standard::Collections;
using Standard::Threading;

namespace Standard::Queries;

// Copies share immutable values. Iterators retain their own share of the storage.
public struct Group<T> : IEnumerable<T, Group<T>::Enumerator>
{
    private struct State
    {
        public long References;
        public Vector<T> Values;
        public State() nothrow
        {
            References = 1l;
            Values = new();
        }
    }

    private State*? state;

    public Group() nothrow { state = null; }

    public copy Group(immutable(Group<T>)* source)
    {
        state = null;
        // Only the reference count changes after the values have been published.
        State*? shared = (State*?)source.state;
        if (shared == null) return;
        long references = Interlocked::Read(ref shared!.References);
        while (true)
        {
            if (references == 9223372036854775807l) throw new Exception("Group reference count overflow");
            long observed = Interlocked::CompareExchange(ref shared!.References, references + 1l, references);
            if (observed == references) break;
            references = observed;
        }
        state = shared;
    }

    public nuint Count
    {
        immutable get nothrow
        {
            if (state == null) return 0;
            return state!.Values.Count;
        }
    }

    public bool IsEmpty { immutable get nothrow => this.Count == 0; }

    internal void Add(T value)
    {
        if (state == null) state = new* State();
        else if (Interlocked::Read(ref state!.References) != 1l)
            throw new Exception("Cannot append to a shared group");
        state!.Values.Add(move value);
    }

    public immutable Enumerator GetEnumerator() => new Enumerator(*this);

    public immutable Enumerable<T, Group<T>, Group<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Group<T>, Group<T>::Enumerator>(*this);

    public struct Enumerator
    {
        private Group<T> owner;
        private nuint position;
        private bool valid;

        public Enumerator(Group<T> group)
        {
            owner = move group;
            position = 0;
            valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (position == owner.Count) return false;
            position++;
            valid = true;
            return true;
        }

        public ref immutable(T) Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return owner.state!.Values[position - 1];
            }
        }
    }

    ~Group()
    {
        State*? released = state;
        state = null;
        if (released != null && Interlocked::Decrement(ref released!.References) == 0l) delete released!;
    }
}
