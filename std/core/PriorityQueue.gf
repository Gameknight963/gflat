namespace std;

// Owns a min-heap. The borrowed comparator returns negative for a higher priority.
// Its ordering must remain consistent while elements are stored in this queue.
struct PriorityQueue<T>
{
    private Vector<T> storage;
    private int(readonly(T)*, readonly(T)*)* compare;

    public PriorityQueue(int(readonly(T)*, readonly(T)*)* comparison)
    {
        storage = new Vector<T>();
        compare = comparison;
    }

    public nuint Count { readonly get nothrow => storage.Count; }
    public nuint Capacity { readonly get nothrow => storage.Capacity; }
    public bool IsEmpty { readonly get nothrow => storage.Count == 0; }
    public void Reserve(nuint capacity) { storage.Reserve(capacity); }
    public void Clear() { storage.Clear(); }

    public ref readonly(T) Top
    {
        get
        {
            if (storage.Count == 0) throw new Exception("Cannot peek an empty priority queue");
            return storage[0];
        }
    }

    public void Enqueue(T value)
    {
        nuint end = storage.Count;
        nuint destination = end;
        // Find the insertion location before changing any existing element.
        while (destination != 0)
        {
            nuint parent = (destination - 1) / 2;
            if (compare(&value, &storage[parent]) >= 0) break;
            destination = parent;
        }
        storage.Add(move value);
        if (destination == end) return;
        Span<T> elements = storage.AsSpan();
        T* data = elements.Data!;
        T pending = move *(data + end);
        nuint hole = end;
        while (hole != destination)
        {
            nuint parent = (hole - 1) / 2;
            move *(data + parent) at data + hole;
            hole = parent;
        }
        move pending at data + destination;
    }

    public T Dequeue()
    {
        nuint count = storage.Count;
        if (count == 0) throw new Exception("Cannot dequeue an empty priority queue");
        if (count == 1) return storage.Pop();
        Span<T> elements = storage.AsSpan();
        T* data = elements.Data!;
        nuint end = count - 1;
        // A binary heap cannot have more levels than the pointer width. Record
        // the path before moving values, so a throwing comparison changes nothing.
        nuint[64] path;
        nuint depth = 0;
        nuint hole = 0;
        while (hole < end / 2)
        {
            nuint child = hole * 2 + 1;
            if (child + 1 < end && compare(data + child + 1, data + child) < 0) child++;
            if (compare(data + child, data + end) >= 0) break;
            path[depth] = child;
            depth++;
            hole = child;
        }
        T result = move *data;
        T last = storage.Pop();
        hole = 0;
        for (nuint i = 0; i < depth; i++)
        {
            nuint child = path[i];
            move *(data + child) at data + hole;
            hole = child;
        }
        move last at data + hole;
        return result;
    }

    public bool TryDequeue(ref T result)
    {
        if (storage.Count == 0) return false;
        Span<T> elements = storage.AsSpan();
        nuint start = (nuint)elements.Data!;
        nuint output = (nuint)&result;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (output >= start && output - start < storage.Capacity * stride)
            throw new Exception("Priority queue output must not refer to its storage");
        result = this.Dequeue();
        return true;
    }
}
