namespace std::Collections;

// An owning FIFO ring buffer. Structural mutation invalidates borrowed elements and iterators.
struct Queue<T> : IEnumerable<T, Queue<T>::Enumerator>
{
    private T*? data;
    private nuint capacity;
    private nuint count;
    private nuint head;

    public Queue() { data = null; capacity = 0; count = 0; head = 0; }
    public nuint Count { immutable get nothrow => count; }
    public nuint Capacity { immutable get nothrow => capacity; }
    public bool IsEmpty { immutable get nothrow => count == 0; }

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
            if (data != null) Memory::Free((void*)data);
            data = null; capacity = 0; head = 0;
            return;
        }
        nuint maximum = (nuint)0 - (nuint)1;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (requested > maximum / stride) throw new Exception("Queue capacity overflow");
        void*? allocation = Memory::Allocate(requested * stride);
        if (allocation == null) throw new BadAllocException("Queue allocation failed");
        T* replacement = (T*)allocation;
        nuint source = head;
        for (nuint i = 0; i < count; i++)
        {
            move *(data! + source) at replacement + i;
            source++;
            if (source == capacity) source = 0;
        }
        if (data != null) Memory::Free((void*)data);
        data = replacement;
        capacity = requested;
        head = 0;
    }

    public void Enqueue(T value)
    {
        if (count == capacity)
        {
            nuint stride = (nuint)sizeof(T);
            if (stride == 0) stride = 1;
            nuint maximum = ((nuint)0 - (nuint)1) / stride;
            if (count == maximum) throw new Exception("Queue capacity overflow");
            nuint grown = 4;
            if (capacity != 0)
            {
                grown = maximum;
                if (capacity <= maximum / 2) grown = capacity * 2;
            }
            if (grown > maximum) grown = maximum;
            this.Reserve(grown);
        }
        // Avoid overflowing head + count even near the largest allocation size.
        nuint remaining = capacity - head;
        nuint tail = 0;
        if (count < remaining) tail = head + count;
        else tail = count - remaining;
        move value at data! + tail;
        count++;
    }

    public T Dequeue()
    {
        if (count == 0) throw new Exception("Cannot dequeue an empty queue");
        T result = move *(data! + head);
        count--;
        head++;
        if (head == capacity || count == 0) head = 0;
        return result;
    }

    public bool TryDequeue(ref T result)
    {
        if (count == 0) return false;
        nuint start = (nuint)data!;
        nuint output = (nuint)&result;
        nuint stride = (nuint)sizeof(T);
        if (stride == 0) stride = 1;
        if (output >= start && output - start < capacity * stride)
            throw new Exception("Queue output must not refer to its storage");
        result = this.Dequeue();
        return true;
    }

    public ref T Front
    {
        immutable get
        {
            if (count == 0) throw new Exception("Cannot peek an empty queue");
            return data![head];
        }
    }

    public void Clear()
    {
        while (count != 0)
        {
            (data! + head).~T();
            count--;
            head++;
            if (head == capacity) head = 0;
        }
        head = 0;
    }

    public Enumerator GetEnumerator() => new Enumerator(data, capacity, head, count);

    public Enumerable<T, Queue<T>::View, Queue<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, Queue<T>::View, Queue<T>::Enumerator>(new View(data, capacity, head, count));

    // Copies only ring metadata; the queue continues to own its elements.
    public struct View : IEnumerable<T, Queue<T>::Enumerator>
    {
        private T*? data;
        private nuint capacity;
        private nuint head;
        private nuint count;
        private View(T*? storage, nuint size, nuint start, nuint length)
        {
            data = storage; capacity = size; head = start; count = length;
        }
        public Queue<T>::Enumerator GetEnumerator() => new Queue<T>::Enumerator(data, capacity, head, count);
    }

    ~Queue()
    {
        this.Clear();
        if (data != null) Memory::Free((void*)data);
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
                throw new Exception("Invalid queue enumerator storage");
            data = storage; capacity = size; position = start; remaining = length; valid = false;
        }

        public bool MoveNext()
        {
            if (remaining == 0) { valid = false; return false; }
            if (valid)
            {
                position++;
                if (position == capacity) position = 0;
            }
            remaining--;
            valid = true;
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
