# Inspecting priority queue items

`PriorityQueue<T>.UnorderedItems` borrows all stored items without removing them
or comparing priorities. Its `Count` and `IsEmpty` reflect the current queue.

```csharp
using Standard::Collections;

int Compare(immutable(int)* left, immutable(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    PriorityQueue<int> queue = new(Compare);
    queue.Enqueue(30);
    queue.Enqueue(10);
    queue.Enqueue(20);
    int sum = 0;
    foreach (immutable(int)* value in queue.UnorderedItems) sum += *value;
    if (sum != 60 || queue.Count != 3 || queue.Top != 10) return 1;
    return 0;
}
```

Enumeration yields `immutable(T)*` pointers in unspecified order, so it supports
noncopyable owners without copying them. The view and its iterator allocate no
storage and never call the comparator. To remove items in priority order, call
`Dequeue` instead. `Current` throws before the first successful `MoveNext` and
after iteration ends.

## Lifetime and mutation

The collection view holds a pointer to the queue object. Keep that object alive
at the same address. Moving or destroying it invalidates saved views. Each new
iterator captures the current heap storage and item count, including after
earlier additions, removals, or capacity changes.

Do not mutate the queue while using an iterator or yielded pointer. Queue
mutations can reorder or relocate its elements. Reacquire borrows afterward.
The immutable pointers prevent edits through the view that could corrupt heap
ordering. Other aliases must also preserve element priorities while enqueued.

## Queries

`queue.UnorderedItems.AsEnumerable()` supports query operators over the same
borrowed pointers. Saved queries observe current contents when enumerated again.
`Select` can project independent values, while a direct `ToVector()` collects
borrowed pointers whose lifetime still depends on the queue.

For explicit source lists, include `Collections/priority_queue_queries.gf` to
enable this optional query extension, along with the query operators used.
Inspecting `UnorderedItems` alone requires only the priority queue's existing
dependencies.
