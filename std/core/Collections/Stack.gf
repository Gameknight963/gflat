namespace std;

// Owns its elements. Views and iterators borrow storage; structural mutation invalidates them.
struct Stack<T> : IEnumerable<T, Stack<T>::Enumerator>
{
    private Vector<T> storage;

    public Stack() { storage = new Vector<T>(); }
    public nuint Count { immutable get nothrow => storage.Count; }
    public nuint Capacity { immutable get nothrow => storage.Capacity; }
    public bool IsEmpty { immutable get nothrow => storage.Count == 0; }

    public void Reserve(nuint capacity) { storage.Reserve(capacity); }
    public void ShrinkToFit() { storage.ShrinkToFit(); }
    public void Push(T value) { storage.Add(move value); }
    public T Pop() => storage.Pop();
    public bool TryPop(ref T result)
    {
        if (storage.Count == 0) return false;
        Span<T> elements = storage.AsSpan();
        nuint start = (nuint)elements.Data!;
        nuint output = (nuint)&result;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (output >= start && output - start < storage.Capacity * stride)
            throw new Exception("Stack output must not refer to its storage");
        result = storage.Pop();
        return true;
    }
    public void Clear() { storage.Clear(); }

    // Borrows the top element instead of copying an owning value.
    public ref T Top
    {
        immutable get
        {
            if (storage.Count == 0) throw new Exception("Cannot peek an empty stack");
            return storage[storage.Count - 1];
        }
    }

    public Enumerator GetEnumerator() => new Enumerator(storage.AsSpan());

    public Enumerable<T, Stack<T>::View, Stack<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Stack<T>::View, Stack<T>::Enumerator>(new View(storage.AsSpan()));

    // The view retains the captured top-to-bottom extent without copying owners.
    public struct View : IEnumerable<T, Stack<T>::Enumerator>
    {
        private Span<T> storage;
        private View(Span<T> elements) { storage = elements; }
        public Stack<T>::Enumerator GetEnumerator() => new Stack<T>::Enumerator(storage);
    }

    // Enumeration proceeds from the top toward the bottom.
    public struct Enumerator
    {
        private Span<T> storage;
        private nuint position;
        private bool valid;
        public Enumerator(Span<T> elements)
        {
            storage = elements;
            position = elements.Length;
            valid = false;
        }

        public bool MoveNext()
        {
            valid = false;
            if (position == 0) return false;
            position--;
            valid = true;
            return true;
        }

        public ref T Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return storage[position];
            }
        }
    }
}
