namespace std;

// Owns contiguous elements. Growth invalidates pointers and spans into the buffer.
struct Vector<T>
{
    private T*? data;
    private nuint count;
    private nuint capacity;

    public Vector() { data = null; count = 0; capacity = 0; }

    public nuint Count { readonly get => count; }
    public nuint Capacity { readonly get => capacity; }

    public ref T this[nuint index]
    {
        get throws
        {
            if (index >= count) throw new* Exception(c"Vector index out of range");
            return data![index];
        }
    }

    public void Reserve(nuint requested) throws
    {
        if (requested <= capacity) return;
        nuint maximum = (nuint)0 - (nuint)1;
        nuint stride = (nuint)sizeof(T);
        if (stride == (nuint)0) stride = 1;
        if (requested > maximum / stride) throw new* Exception(c"Vector capacity overflow");
        void*? allocation = Allocator::Allocate(requested * stride);
        if (allocation == null) throw new* Exception(c"Vector allocation failed");
        T* replacement = (T*)allocation;
        for (nuint i = 0; i < count; i++)
        {
            move *(data! + i) at replacement + i;
        }
        if (data != null) Allocator::Free((void*)data);
        data = replacement;
        capacity = requested;
    }

    public void Add(T value) throws
    {
        if (count == capacity)
        {
            nuint stride = (nuint)sizeof(T);
            if (stride == (nuint)0) stride = 1;
            nuint maximum = ((nuint)0 - (nuint)1) / stride;
            if (count == maximum) throw new* Exception(c"Vector capacity overflow");
            nuint grown = 4;
            if (capacity != (nuint)0)
            {
                grown = maximum;
                if (capacity <= maximum / (nuint)2) grown = capacity * (nuint)2;
            }
            if (grown > maximum) grown = maximum;
            this.Reserve(grown);
        }
        move value at data! + count;
        count++;
    }

    public T Pop() throws
    {
        if (count == (nuint)0) throw new* Exception(c"Cannot pop an empty vector");
        nuint index = count - (nuint)1;
        T result = move *(data! + index);
        count = index;
        return result;
    }

    public void Clear()
    {
        while (count != (nuint)0)
        {
            count--;
            (data! + count).~T();
        }
    }

    ~Vector()
    {
        this.Clear();
        if (data != null) Allocator::Free((void*)data);
    }
}
