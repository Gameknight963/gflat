using Standard::Threading;

namespace Standard::Memory;

// Internal ownership for immutable published collections and their construction state.
internal struct SharedStorage<T>
{
    private struct State
    {
        public long References;
        public T Value;
        public State(T input)
        {
            References = 1l;
            Value = move input;
        }
    }

    private State*? state;

    public SharedStorage() nothrow { state = null; }
    public SharedStorage(T input) { state = new* State(move input); }

    public copy SharedStorage(immutable(SharedStorage<T>)* source)
    {
        state = null;
        // Reference counts change independently of the published payload.
        State*? shared = (State*?)source.state;
        if (shared == null) return;
        long references = Interlocked::Read(ref shared!.References);
        while (true)
        {
            if (references == 9223372036854775807l) throw new Exception("Shared reference count overflow");
            long observed = Interlocked::CompareExchange(ref shared!.References, references + 1l, references);
            if (observed == references) break;
            references = observed;
        }
        state = shared;
    }

    public bool IsEmpty { immutable get nothrow => state == null; }
    public bool IsUnique
    {
        immutable get nothrow
        {
            if (state == null) return true;
            State* shared = (State*)state!;
            return Interlocked::Read(ref shared.References) == 1l;
        }
    }

    public ref T Value
    {
        get
        {
            if (state == null) throw new Exception("Shared storage is empty");
            return state!.Value;
        }
    }

    public ref immutable(T) ValueImmutable
    {
        immutable get
        {
            if (state == null) throw new Exception("Shared storage is empty");
            return state!.Value;
        }
    }

    ~SharedStorage()
    {
        State*? released = state;
        state = null;
        if (released != null && Interlocked::Decrement(ref released!.References) == 0l) delete released!;
    }
}
