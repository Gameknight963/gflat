namespace std;

// Owns contiguous elements. Growth invalidates pointers and spans into the buffer.
struct Vector<T> : IEnumerable<T, Span<T>::Enumerator>
{
    private T*? data;
    private nuint count;
    private nuint capacity;

    public Vector() nothrow { data = null; count = 0; capacity = 0; }

    public nuint Count { immutable get nothrow => count; }
    public nuint Capacity { immutable get nothrow => capacity; }
    public bool IsEmpty { immutable get nothrow => count == 0; }
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

    public immutable Span<immutable(T)> AsImmutable()
    {
        if (count == 0) return new Span<immutable(T)>();
        return new Span<immutable(T)>((immutable(T)*)data, count);
    }

    public ref T this[nuint index]
    {
        immutable get
        {
            if (index >= count) throw new Exception("Vector index out of range");
            return data![index];
        }
    }

    public void Reserve(nuint requested)
    {
        if (requested <= capacity) return;
        this.RelocateStorage(requested);
    }

    // Release unused peak storage explicitly; structural borrows are invalidated.
    public void ShrinkToFit()
    {
        if (count == capacity) return;
        this.RelocateStorage(count);
    }

    private void RelocateStorage(nuint requested)
    {
        if (requested == 0)
        {
            if (data != null) Memory::Free((void*)data);
            data = null; capacity = 0;
            return;
        }
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

    private void EnsureCapacityForOne()
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
    }

    public void Add(T value)
    {
        this.EnsureCapacityForOne();
        move value at data! + count;
        count++;
    }

    public void Insert(nuint index, T value)
    {
        if (index > count) throw new Exception("Vector insertion index out of range");
        this.EnsureCapacityForOne();
        nuint hole = count;
        while (hole != index)
        {
            move *(data! + hole - 1) at data! + hole;
            hole--;
        }
        move value at data! + index;
        count++;
    }

    // Transfer the removed element to the caller, like Pop at an arbitrary index.
    public T RemoveAt(nuint index)
    {
        if (index >= count) throw new Exception("Vector removal index out of range");
        T result = move *(data! + index);
        for (nuint source = index + 1; source < count; source++)
            move *(data! + source) at data! + source - 1;
        count--;
        return result;
    }

    // Destroy a window and shift its suffix once; no copies or allocations.
    public void RemoveRange(nuint start, nuint length)
    {
        if (start > count || length > count - start)
            throw new Exception("Vector removal range out of bounds");
        if (length == 0) return;
        nuint end = start + length;
        nuint remaining = end;
        while (remaining > start)
        {
            remaining--;
            (data! + remaining).~T();
        }
        for (nuint source = end; source < count; source++)
            move *(data! + source) at data! + source - length;
        count -= length;
    }

    // Stable retained prefix; matched owners stay initialized until cleanup.
    public nuint RemoveAll(bool(immutable(T)*)* predicate)
    {
        nuint originalCount = count;
        nuint read = 0;
        nuint write = 0;
        try
        {
            while (read < originalCount)
            {
                if (!predicate(data! + read))
                {
                    if (read != write)
                    {
                        T retained = move *(data! + read);
                        move *(data! + write) at data! + read;
                        move retained at data! + write;
                    }
                    write++;
                }
                read++;
            }
        }
        catch
        {
            this.RemoveRange(write, read - write);
            throw;
        }
        this.RemoveRange(write, originalCount - write);
        return originalCount - write;
    }

    // Constant-time removal when preserving element order is unnecessary.
    public T SwapRemoveAt(nuint index)
    {
        if (index >= count) throw new Exception("Vector removal index out of range");
        nuint last = count - 1;
        T result = move *(data! + index);
        if (index != last) move *(data! + last) at data! + index;
        count = last;
        return result;
    }

    public T Pop()
    {
        if (count == 0) throw new Exception("Cannot pop an empty vector");
        nuint index = count - 1;
        T result = move *(data! + index);
        count = index;
        return result;
    }

    public void Truncate(nuint length)
    {
        if (length > count) throw new Exception("Vector truncation length out of range");
        while (count > length)
        {
            count--;
            (data! + count).~T();
        }
    }

    public void Clear() { this.Truncate(0); }

    ~Vector()
    {
        this.Clear();
        if (data != null) Memory::Free((void*)data);
    }
}
