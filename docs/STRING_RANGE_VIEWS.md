# Borrowing a range of string bytes

Load `Standard/core/Text/string_span.gf` for `AsSpan`, `AsReadOnly`, and
`AsImmutable` overloads accepting `start` or `start, count` byte indexes.

```csharp
using Standard::Text;
using Standard::Memory;

int main()
{
    String text = s"name=value";
    Span<readonly(char)> name = text.AsReadOnly(0, 4);
    Span<char> value = text.AsSpan(5);
    value[0] = 'V';
    if (name.Length != 4 || name[0] != 'n' || !text.Equals("name=Value")) return 1;
    return 0;
}
```

`AsSpan` exposes mutable bytes and requires a mutable owner. `AsReadOnly`
accepts a readonly owner, and `AsImmutable` accepts an immutable owner. Both
protected forms return `Span<readonly(char)>`, preserving the existing string
view contract. Other aliases can still change the underlying bytes.
Freestanding forms take the corresponding `ref String` qualification first.

Ranges include embedded zeros and exclude the trailing terminator and spare
capacity. `start == Length` is valid only with zero count or for an empty
suffix. Invalid ranges throw without modifying the string. Empty ranges
return an empty span with a null data pointer.

These operations copy only span metadata and do not allocate or copy string
bytes. Keep the owner alive and its storage stable. Growth, moves, destruction,
and removal of viewed bytes can invalidate a view. Returning a view from a
temporary String receiver does not extend the owner's lifetime.
