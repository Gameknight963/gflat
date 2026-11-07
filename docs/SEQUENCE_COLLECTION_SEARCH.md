# Searching vectors and deques

Load `Standard/core/Collections/vector_search.gf` or `deque_search.gf` for
`Contains`, `IndexOf`, `LastIndexOf`, and `RemoveValue`. The default overloads
use `IEquatable<T>`. Custom equality overloads accept
`bool(immutable(T)*, immutable(T)*)*` and do not require that interface.
Freestanding forms accept a pointer to the collection first.

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    values.Add(10); values.Add(20); values.Add(10);
    int key = 10;
    if (values.IndexOf(&key) != 0 || values.LastIndexOf(&key) != 2) return 1;
    if (!values.RemoveValue(&key) || values.Count != 2 || values[0] != 20) return 2;
    key = 99;
    if (values.Contains(&key) || values.IndexOf(&key) != values.Count) return 3;
    return 0;
}
```

An absent index is `Count`, following the unsigned-index span search convention.
`IndexOf` and `Contains` search front to back; `LastIndexOf` searches back to
front. All borrow stored elements without copying them and stop at the first
match in their search direction. Empty collections do not invoke equality.

`RemoveValue` removes and destroys the first matching element and returns true.
An absent value returns false. Retained elements keep their relative order.
The key may borrow an element from the collection because it is no longer
read once removal starts. Removal can move retained elements and invalidate
borrows and iterators. A destructor failure can propagate after removal.

Comparisons that throw leave the collection untouched. Comparators must not
structurally mutate or destroy the collection during search. These operations
support noncopyable owners. Successful searches and removal allocate no storage
apart from work performed by user callbacks or destructors. Search is linear;
removal can additionally relocate the retained suffix.
