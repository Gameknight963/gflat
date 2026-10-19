namespace std;

// Owns contiguous elements. Growth invalidates pointers and spans into the buffer.
struct Vector<T> : IEnumerable<T, Span<T>::Enumerator>
{
    private T*? data;
    private nuint count;
    private nuint capacity;

    public Vector() { data = null; count = 0; capacity = 0; }

    public nuint Count { readonly get => count; }
    public nuint Capacity { readonly get => capacity; }
    public Span<T>::Enumerator GetEnumerator() => new Span<T>::Enumerator(data, count);
    public Enumerable<T, Span<T>, Span<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Span<T>, Span<T>::Enumerator>(this.AsSpan());

    public Span<T> AsSpan()
    {
        if (count == 0) return new Span<T>();
        return new Span<T>(data!, count);
    }

    public readonly Span<readonly(T)> AsReadOnly()
    {
        if (count == 0) return new Span<readonly(T)>();
        return new Span<readonly(T)>((readonly(T)*)data, count);
    }

    public ref T this[nuint index]
    {
        get
        {
            if (index >= count) throw new Exception("Vector index out of range");
            return data![index];
        }
    }

    public void Reserve(nuint requested)
    {
        if (requested <= capacity) return;
        nuint maximum = (nuint)0 - (nuint)1;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (requested > maximum / stride) throw new Exception("Vector capacity overflow");
        void*? allocation = Memory::Allocate(requested * stride);
        if (allocation == null) throw new BadAllocException("Vector allocation failed");
        T* replacement = (T*)allocation;
        for (nuint i = 0; i < count; i++)
        {
            move *(data! + i) at replacement + i;
        }
        if (data != null) Memory::Free((void*)data);
        data = replacement;
        capacity = requested;
    }

    public void Add(T value)
    {
        if (count == capacity)
        {
            nuint stride = (nuint)sizeof(T);
            if (stride == 0) stride = 1;
            nuint maximum = ((nuint)0 - (nuint)1) / stride;
            if (count == maximum) throw new Exception("Vector capacity overflow");
            nuint grown = 4;
            if (capacity != 0)
            {
                grown = maximum;
                if (capacity <= maximum / 2) grown = capacity * 2;
            }
            if (grown > maximum) grown = maximum;
            this.Reserve(grown);
        }
        move value at data! + count;
        count++;
    }

    public T Pop()
    {
        if (count == 0) throw new Exception("Cannot pop an empty vector");
        nuint index = count - 1;
        T result = move *(data! + index);
        count = index;
        return result;
    }

    public void Clear()
    {
        while (count != 0)
        {
            count--;
            (data! + count).~T();
        }
    }

    ~Vector()
    {
        this.Clear();
        if (data != null) Memory::Free((void*)data);
    }
}
