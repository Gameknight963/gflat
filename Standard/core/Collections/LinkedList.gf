using Standard::Queries;
namespace Standard::Collections;

// Owns nodes and values. Borrowed node addresses survive insertion and list moves.
struct LinkedList<T> : IEnumerable<T, LinkedList<T>::Enumerator>
{
    private State*? state;

    // A stable identity lets nodes remain associated with their owner when the
    // list value moves. Empty construction does not allocate this state.
    private struct State
    {
        public Node*? First;
        public Node*? Last;
        public nuint Count;
        public State() { First = null; Last = null; Count = 0; }
    }

    public struct Node
    {
        private State* owner;
        private Node*? next;
        private Node*? previous;
        private T value;

        private Node(State* list, T item, Node*? before, Node*? after)
        {
            owner = list; previous = before; next = after; value = move item;
        }

        public Node*? Next { get => next; }
        public Node*? Previous { get => previous; }
        public ref T Value { immutable get => value; }

        // A node is an identity-bearing allocation, not a copyable value.
        // Field destruction still cleans value when the list deletes the node.
        ~Node() {}
    }

    public LinkedList() { state = null; }
    public nuint Count
    {
        immutable get nothrow
        {
            if (state == null) return 0;
            return state!.Count;
        }
    }
    public bool IsEmpty { immutable get nothrow => this.Count == 0; }
    public Node*? First
    {
        get
        {
            if (state == null) return null;
            return state!.First;
        }
    }
    public Node*? Last
    {
        get
        {
            if (state == null) return null;
            return state!.Last;
        }
    }

    private State* EnsureState()
    {
        if (state == null) state = new* State();
        return state!;
    }

    private void CheckCount()
    {
        if (this.Count == (nuint)0 - (nuint)1) throw new Exception("LinkedList count overflow");
    }

    private State* CheckNode(Node* node)
    {
        if (state == null || node.owner != state) throw new Exception("Node belongs to another linked list");
        return state!;
    }

    public Node* AddFirst(T value)
    {
        this.CheckCount();
        State* list = this.EnsureState();
        Node* node = new* Node(list, move value, null, list.First);
        if (list.First != null) list.First!.previous = node;
        else list.Last = node;
        list.First = node;
        list.Count++;
        return node;
    }

    public Node* AddLast(T value)
    {
        this.CheckCount();
        State* list = this.EnsureState();
        Node* node = new* Node(list, move value, list.Last, null);
        if (list.Last != null) list.Last!.next = node;
        else list.First = node;
        list.Last = node;
        list.Count++;
        return node;
    }

    public Node* InsertBefore(Node* position, T value)
    {
        State* list = this.CheckNode(position);
        this.CheckCount();
        Node* node = new* Node(list, move value, position.previous, position);
        if (position.previous != null) position.previous!.next = node;
        else list.First = node;
        position.previous = node;
        list.Count++;
        return node;
    }

    public Node* InsertAfter(Node* position, T value)
    {
        State* list = this.CheckNode(position);
        this.CheckCount();
        Node* node = new* Node(list, move value, position, position.next);
        if (position.next != null) position.next!.previous = node;
        else list.Last = node;
        position.next = node;
        list.Count++;
        return node;
    }

    private void Unlink(Node* node)
    {
        State* list = state!;
        if (node.previous != null) node.previous!.next = node.next;
        else list.First = node.next;
        if (node.next != null) node.next!.previous = node.previous;
        else list.Last = node.previous;
        list.Count--;
    }

    public T Remove(Node* node)
    {
        this.CheckNode(node);
        // Explicit raw-storage movement ends the value slot's lifetime. The
        // remaining header fields are plain pointers, so free the node storage
        // directly instead of destroying the transferred value a second time.
        T result = move *(&node.value);
        this.Unlink(node);
        global::Memory::Free((void*)node);
        return result;
    }

    public T RemoveFirst()
    {
        if (state == null || state!.First == null) throw new Exception("Cannot remove from an empty linked list");
        return this.Remove(state!.First!);
    }

    public T RemoveLast()
    {
        if (state == null || state!.Last == null) throw new Exception("Cannot remove from an empty linked list");
        return this.Remove(state!.Last!);
    }

    public nuint RemoveAll(bool(immutable(T)*)* predicate)
    {
        nuint removed = 0;
        Node*? current = this.First;
        while (current != null)
        {
            Node* node = current!;
            Node*? next = node.next;
            if (predicate(&node.value))
            {
                this.Unlink(node);
                delete node;
                removed++;
            }
            current = next;
        }
        return removed;
    }

    // Relink nodes without moving their values or invalidating node addresses.
    public void Reverse() nothrow
    {
        if (state == null) return;
        Node*? current = state!.First;
        while (current != null)
        {
            Node* node = current!;
            Node*? next = node.next;
            node.next = node.previous;
            node.previous = next;
            current = next;
        }
        Node*? first = state!.First;
        state!.First = state!.Last;
        state!.Last = first;
    }

    public void Clear()
    {
        while (state != null && state!.First != null)
        {
            Node* node = state!.First!;
            this.Unlink(node);
            delete node;
        }
    }

    public Enumerator GetEnumerator() => new Enumerator(this.First);

    public Enumerable<T, LinkedList<T>::View, LinkedList<T>::Enumerator> AsEnumerable()
        => new Enumerable<T, LinkedList<T>::View, LinkedList<T>::Enumerator>(new View(this.First));

    // Captures the first node; moving the owner leaves these nodes in place.
    public struct View : IEnumerable<T, LinkedList<T>::Enumerator>
    {
        private Node*? first;
        private View(Node*? node) { first = node; }
        public LinkedList<T>::Enumerator GetEnumerator() => new LinkedList<T>::Enumerator(first);
    }

    ~LinkedList()
    {
        this.Clear();
        if (state != null) delete state!;
    }

    public struct Enumerator
    {
        private Node*? next;
        private Node*? current;
        public Enumerator(Node*? first) { next = first; current = null; }
        public bool MoveNext()
        {
            current = next;
            if (current == null) return false;
            next = current!.Next;
            return true;
        }
        public ref T Current
        {
            immutable get
            {
                if (current == null) throw new Exception("Enumerator has no current element");
                return current!.Value;
            }
        }
    }
}
