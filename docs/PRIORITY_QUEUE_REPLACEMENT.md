# Replacing a priority queue root

`PriorityQueue<T>` supplies two combined operations that keep the queue's count
and capacity unchanged. Both return an owning value.

| Method | Behavior |
| --- | --- |
| `EnqueueDequeue(value)` | Return the minimum of the incoming value and the queue's elements, retaining the rest |
| `DequeueEnqueue(value)` | Return the existing minimum and insert the incoming value |

`EnqueueDequeue` returns the incoming value when the queue is empty. If it
compares equal to the current root, the incoming value is returned and the
existing root stays in the queue. `DequeueEnqueue` always replaces the root,
including when priorities compare equal, and throws on an empty queue.

## Keep the largest candidates

Fill a min-heap to the desired size, then use `EnqueueDequeue` for subsequent
candidates. The root is the smallest retained candidate.

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
    PriorityQueue<int> best = new(Compare);
    best.Enqueue(20);
    best.Enqueue(5);
    best.Enqueue(12);
    int discarded = best.EnqueueDequeue(30); // 5
    if (discarded != 5 || best.Count != 3 || best.Top != 12) return 1;
    discarded = best.EnqueueDequeue(2); // Incoming candidate is too small
    if (discarded != 2 || best.Top != 12) return 2;
    int previous = best.DequeueEnqueue(1); // Replace the root unconditionally
    if (previous != 12 || best.Top != 1 || best.Count != 3) return 3;
    return 0;
}
```

## Ownership, cost, and errors

Pass `move value` to transfer an existing owner. An incoming value is either
retained by the queue or returned to the caller. Ignoring the returned value
destroys it as an ordinary temporary.

The combined operations use at most one sift-down, with logarithmic comparison
and relocation work. They do not grow or allocate queue storage. Argument
construction and the comparator may still allocate or throw.

Comparisons finish before the queue moves any stored value. If a comparison
throws, queue contents, count, capacity, and existing element addresses remain
unchanged. The incoming argument is destroyed during unwinding. A successful
replacement can relocate elements, so reacquire borrowed element references.
Comparators must leave the queue and its values unchanged.
