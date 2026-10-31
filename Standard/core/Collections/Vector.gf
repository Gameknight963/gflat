using Standard::Memory;
using Standard::Queries;

namespace Standard::Collections;

// Owns contiguous elements.
// Buffer relocation invalidates element pointers, spans, and iterators.
// Insertion and removal can move elements. Existing views keep their original ranges.
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

    // Borrows the current elements. Keep the vector alive and its buffer stable.
    // Later Count changes leave the captured span length unchanged.
    public Span<T> AsSpan()
    {
        if (count == 0) return new Span<T>();
        return new Span<T>(data!, count);
    }

    // Borrows a shallow readonly view of the current elements.
    public readonly Span<readonly(T)> AsReadOnly()
    {
        if (count == 0) return new Span<readonly(T)>();
        return new Span<readonly(T)>((readonly(T)*)data, count);
    }

    // Borrows an immutable view. Keep the vector alive and its buffer stable.
    // Other aliases can still mutate elements. The view keeps its captured length.
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

    // Ensures capacity is at least requested without changing Count.
    // Relocation invalidates borrows. Allocation failure preserves existing contents.
    public void Reserve(nuint requested)
    {
        if (requested <= capacity) return;
        this.RelocateStorage(requested);
    }

    // Reduces Capacity to Count, releasing an empty buffer.
    // Relocation invalidates borrows. Allocation failure preserves the original buffer.
    public void ShrinkToFit()
    {
        if (count == capacity) return;
        this.RelocateStorage(count);
    }

    private void RelocateStorage(nuint requested)
    {
        if (requested == 0)
        {
            if (data != null) global::Memory::Free((void*)data);
            data = null; capacity = 0;
            return;
        }
        nuint maximum = (nuint)0 - (nuint)1;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (requested > maximum / stride) throw new Exception("Vector capacity overflow");
        void*? allocation = global::Memory::Allocate(requested * stride);
        if (allocation == null) throw new BadAllocException("Vector allocation failed");
        T* replacement = (T*)allocation;
        for (nuint i = 0; i < count; i++)
        {
            move *(data! + i) at replacement + i;
        }
        if (data != null) global::Memory::Free((void*)data);
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

    // Appends a value. Capacity growth invalidates buffer borrows.
    // Use move to transfer a named noncopyable owner, not an element already stored here.
    public void Add(T value)
    {
        this.EnsureCapacityForOne();
        move value at data! + count;
        count++;
    }

    // Inserts before index, allowing Count for append, and preserves survivor order.
    // Growth invalidates all borrows. Otherwise elements at and after index move.
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

    // Removes and returns the selected value, shifting following elements left.
    public T RemoveAt(nuint index)
    {
        if (index >= count) throw new Exception("Vector removal index out of range");
        T result = move *(data! + index);
        for (nuint source = index + 1; source < count; source++)
            move *(data! + source) at data! + source - 1;
        count--;
        return result;
    }

    // Destroys the range and shifts survivors left, retaining capacity.
    // The full range is validated before removal.
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

    // Removes predicate matches, preserving survivor order. Returns the removed count.
    // Tests in order. On predicate failure, earlier matches are removed and the failing suffix remains.
    // Callbacks must not structurally mutate the vector. Reacquire views after removal.
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

    // Removes and returns the selected value, filling its slot from the last element.
    // Runs in constant time and can change survivor order.
    public T SwapRemoveAt(nuint index)
    {
        if (index >= count) throw new Exception("Vector removal index out of range");
        nuint last = count - 1;
        T result = move *(data! + index);
        if (index != last) move *(data! + last) at data! + index;
        count = last;
        return result;
    }

    // Removes and returns the last element, retaining capacity. Throws when empty.
    public T Pop()
    {
        if (count == 0) throw new Exception("Cannot pop an empty vector");
        nuint index = count - 1;
        T result = move *(data! + index);
        count = index;
        return result;
    }

    // Transfers the last element into result, or returns false with result unchanged when empty.
    // The result must be separate from the vector and its backing allocation.
    // Count is updated before the previous result is destroyed.
    public bool TryPop(ref T result)
    {
        if (count == 0) return false;
        nuint start = (data!).reinterpret<nuint>();
        nuint output = (&result).reinterpret<nuint>();
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (output >= start && output - start < capacity * stride)
            throw new Exception("Vector output must not refer to its storage");
        result = this.Pop();
        return true;
    }

    // Destroys the suffix after length, retaining capacity. Length cannot exceed Count.
    public void Truncate(nuint length)
    {
        if (length > count) throw new Exception("Vector truncation length out of range");
        while (count > length)
        {
            count--;
            (data! + count).~T();
        }
    }

    // Destroys all elements while retaining allocated capacity.
    public void Clear() { this.Truncate(0); }

    ~Vector()
    {
        this.Clear();
        if (data != null) global::Memory::Free((void*)data);
    }
}
