# Visiting vector elements without copies

Load `Collections/vector_for_each.gf` for `Vector<T>.ForEach(action)`.
The callback receives an `immutable(T)*` borrowing each stored element, in index
order. This supports noncopyable owners without making temporary element copies.

```csharp
using Standard::Collections;

int sum = 0;
void Add(immutable(int)* value) { sum += *value; }

int main()
{
    Vector<int> values = new();
    values.Add(10);
    values.Add(20);
    values.ForEach(Add);
    if (sum != 30) return 1;
    return 0;
}
```

Empty vectors make no callbacks. The helper allocates nothing and does not move
or replace stored elements. If a callback throws, iteration stops immediately
and completed callback effects remain. The vector remains owned by the caller.

Callbacks must not structurally mutate, move, or destroy the source through other
aliases. Retained element pointers follow the vector's usual storage lifetime
rules. The free `ForEach` function accepts an `immutable(Vector<T>)*` under
`Standard::Collections`. Unlike Enumerable's value-taking `ForEach`, this form
passes borrowed element pointers.
