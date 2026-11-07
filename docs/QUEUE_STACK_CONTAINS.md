# Searching a queue or stack

Load `Standard/core/Collections/queue_search.gf` or `stack_search.gf` for
borrowed `Contains` searches. The default overload uses `IEquatable<T>`.
An overload accepting `bool(immutable(T)*, immutable(T)*)*` supports a custom
comparison without requiring that interface. Both also have freestanding forms.

```csharp
using Standard::Collections;

int main()
{
    Queue<int> queue = new();
    Stack<int> stack = new();
    queue.Enqueue(10);
    stack.Push(20);
    int key = 10;
    if (!queue.Contains(&key) || stack.Contains(&key)) return 1;
    if (queue.Dequeue() != 10 || stack.Pop() != 20) return 2;
    return 0;
}
```

Searches lend each stored element to the comparison and never copy it. This
supports noncopyable owners. Queue searches proceed from front to back, and
stack searches from top to bottom, stopping at the first match. An empty
collection returns false without calling the comparator.

The search itself does not allocate or mutate the collection. Comparison
exceptions propagate without consuming elements. A comparator must not
structurally mutate or destroy the collection while a search is active.
The key can borrow a stored element for the duration of the call.
Search takes at most `Count` comparisons.
