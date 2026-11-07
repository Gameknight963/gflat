# Searching part of a vector

The extensions in `Collections/vector_search.gf` include:

- `IndexOf(key, start)` searches from `start` through the end
- `IndexOf(key, start, count)` searches exactly that forward range
- Both forms accept a final borrowed equality callback

```csharp
using Standard::Collections;

int main()
{
    Vector<int> values = new();
    values.Add(10); values.Add(20); values.Add(10);
    int key = 10;
    if (values.IndexOf(&key, 1) != 2) return 1;
    if (values.IndexOf(&key, 1, 1) != values.Count) return 2;
    return 0;
}
```

Matches return absolute indices in the vector. No match returns the full
vector's `Count`, even for a smaller search range. An empty range at `Count` is
valid and makes no equality calls. Invalid ranges throw before calling equality.

Default equality requires `IEquatable<T>`. Custom equality receives borrowed
`immutable(T)*` pointers, supporting noncopyable elements. The helper copies no
elements and allocates no storage. Callbacks must not structurally mutate the
source. A thrown callback stops the search without changing the vector, while
its earlier external effects remain.

Load `Collections/EqualityComparer.gf` with the extension file. Free-function
forms accept an `immutable(Vector<T>)*` under `Standard::Collections`.
