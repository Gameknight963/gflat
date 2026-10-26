using std::Queries;
namespace std::Collections;

// Owns a ring buffer. Structural mutation invalidates borrowed values/iterators.
struct Deque<T> : IEnumerable<T, Deque<T>::Enumerator>
{
    private T*? data;
    private nuint count;
    private nuint capacity;
    private nuint head;

    public Deque() { data = null; count = 0; capacity = 0; head = 0; }
    public nuint Count { immutable get nothrow => count; }
    public nuint Capacity { immutable get nothrow => capacity; }
    public bool IsEmpty { immutable get nothrow => count == 0; }

    private immutable nuint Physical(nuint offset) nothrow
    {
        nuint remaining = capacity - head;
        if (offset < remaining) return head + offset;
        return offset - remaining;
    }

    public void Reserve(nuint requested)
    {
        if (requested <= capacity) return;
        this.RelocateStorage(requested);
    }

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
            data = null; capacity = 0; head = 0;
            return;
        }
        nuint maximum = (nuint)0 - (nuint)1;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (requested > maximum / stride) throw new Exception("Deque capacity overflow");
        void*? allocation = global::Memory::Allocate(requested * stride);
        if (allocation == null) throw new BadAllocException("Deque allocation failed");
        T* replacement = (T*)allocation;
        for (nuint i = 0; i < count; i++) move *(data! + this.Physical(i)) at replacement + i;
        if (data != null) global::Memory::Free((void*)data);
        data = replacement; capacity = requested; head = 0;
    }

    private void EnsureCapacity()
    {
        if (count < capacity) return;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        nuint maximum = ((nuint)0 - (nuint)1) / stride;
        if (count == maximum) throw new Exception("Deque capacity overflow");
        nuint grown = 4;
        if (capacity != 0)
        {
            grown = maximum;
            if (capacity <= maximum / 2) grown = capacity * 2;
        }
        if (grown > maximum) grown = maximum;
        this.Reserve(grown);
    }

    public void AddFirst(T value)
    {
        this.EnsureCapacity();
        if (count == 0) head = 0;
        else if (head == 0) head = capacity - 1;
        else head--;
        move value at data! + head;
        count++;
    }

    public void AddLast(T value)
    {
        this.EnsureCapacity();
        move value at data! + this.Physical(count);
        count++;
    }

    public void Insert(nuint index, T value)
    {
        if (index > count) throw new Exception("Deque insertion index out of range");
        if (index == 0) { this.AddFirst(move value); return; }
        if (index == count) { this.AddLast(move value); return; }
        // Growth can replace storage and reset head. Compute slots only afterwards.
        this.EnsureCapacity();
        nuint hole = count;
        while (hole != index)
        {
            move *(data! + this.Physical(hole - 1)) at data! + this.Physical(hole);
            hole--;
        }
        move value at data! + this.Physical(index);
        count++;
    }

    public T RemoveAt(nuint index)
    {
        if (index >= count) throw new Exception("Deque removal index out of range");
        if (index == 0) return this.RemoveFirst();
        if (index == count - 1) return this.RemoveLast();
        T result = move *(data! + this.Physical(index));
        for (nuint source = index + 1; source < count; source++)
            move *(data! + this.Physical(source)) at data! + this.Physical(source - 1);
        count--;
        return move result;
    }

    public T RemoveFirst()
    {
        if (count == 0) throw new Exception("Cannot remove from an empty deque");
        T result = move *(data! + head);
        count--; head++;
        if (head == capacity || count == 0) head = 0;
        return result;
    }

    public T RemoveLast()
    {
        if (count == 0) throw new Exception("Cannot remove from an empty deque");
        T result = move *(data! + this.Physical(count - 1));
        count--;
        if (count == 0) head = 0;
        return result;
    }

    // Call only for a nonempty deque. Check the entire allocation, including
    // unused ring slots, before transferring an owner or changing position.
    private void CheckRemovalOutput(T* output)
    {
        nuint start = (nuint)data!;
        nuint address = (nuint)output;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (address >= start && address - start < capacity * stride)
            throw new Exception("Deque output must not refer to its storage");
    }

    public bool TryRemoveFirst(ref T result)
    {
        if (count == 0) return false;
        this.CheckRemovalOutput(&result);
        result = this.RemoveFirst();
        return true;
    }

    public bool TryRemoveLast(ref T result)
    {
        if (count == 0) return false;
        this.CheckRemovalOutput(&result);
        result = this.RemoveLast();
        return true;
    }

    public ref T this[nuint index]
    {
        immutable get
        {
            if (index >= count) throw new Exception("Deque index out of range");
            return data![this.Physical(index)];
        }
    }

    public ref T Front
    {
        immutable get
        {
            if (count == 0) throw new Exception("Cannot peek an empty deque");
            return data![head];
        }
    }

    public ref T Back
    {
        immutable get
        {
            if (count == 0) throw new Exception("Cannot peek an empty deque");
            return data![this.Physical(count - 1)];
        }
    }

    public void Clear()
    {
        while (count != 0)
        {
            (data! + head).~T();
            count--; head++;
            if (head == capacity) head = 0;
        }
        head = 0;
    }

    public Enumerator GetEnumerator() => new Enumerator(data, capacity, head, count);

    public Enumerable<T, Deque<T>::View, Deque<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Deque<T>::View, Deque<T>::Enumerator>(new View(data, capacity, head, count));

    ~Deque() { this.Clear(); if (data != null) global::Memory::Free((void*)data); }

    // Captures ring metadata, borrowing storage without copying the owning deque.
    public struct View : IEnumerable<T, Deque<T>::Enumerator>
    {
        private T*? data;
        private nuint capacity;
        private nuint head;
        private nuint count;
        private View(T*? storage, nuint size, nuint start, nuint length)
        {
            data = storage; capacity = size; head = start; count = length;
        }
        public Deque<T>::Enumerator GetEnumerator() => new Deque<T>::Enumerator(data, capacity, head, count);
    }

    public struct Enumerator
    {
        private T*? data;
        private nuint capacity;
        private nuint position;
        private nuint remaining;
        private bool valid;
        public Enumerator(T*? storage, nuint size, nuint start, nuint length)
        {
            if (length > size || (length != 0 && (storage == null || start >= size)))
                throw new Exception("Invalid deque enumerator storage");
            data = storage; capacity = size; position = start; remaining = length; valid = false;
        }
        public bool MoveNext()
        {
            if (remaining == 0) { valid = false; return false; }
            if (valid) { position++; if (position == capacity) position = 0; }
            remaining--; valid = true;
            return true;
        }
        public ref T Current
        {
            immutable get
            {
                if (!valid) throw new Exception("Enumerator has no current element");
                return data![position];
            }
        }
    }
}
