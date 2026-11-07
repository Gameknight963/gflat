# Building strings from repeated bytes

`String::From(value, count)` creates an owning string containing `count` copies
of a `char`. `text.Append(value, repeatCount)` appends repeated copies in place.
Counts are bytes, and a NUL byte is counted data. Use UTF-8 text operations when
working with complete Unicode scalars rather than individual bytes.

```csharp
using Standard::Text;

int main()
{
    String border = String::From('-', 8);
    if (!border.Equals("--------")) return 1;

    String text = s"item";
    text.EnsureCapacity(32);
    text.Append('.', 3);
    if (!text.Equals("item...")) return 2;
    text.Remove(4);
    if (!text.Equals("item")) return 3;
    return 0;
}
```

## Capacity and removal

`EnsureCapacity(requested)` returns the resulting byte capacity without changing
the contents. It grows storage only when the requested capacity is larger.
Repeated appends reuse spare capacity and grow geometrically when necessary.

`Remove(start)` removes the suffix beginning at the byte index `start`, retaining
capacity. An index equal to `Length` is a no-op. A larger index throws. To remove
a range while retaining its suffix, use `Remove(start, count)`.

## Empty inputs and failures

Zero repetitions create an empty string or leave the append destination
unchanged, without allocating. Suffix removal does not allocate on success.
Each operation maintains the trailing NUL outside `Length`.

Length overflow, an invalid removal index, or allocation failure throws. Failed
growth preserves contents, capacity, and existing borrows. Successful growth
invalidates borrows into the old buffer. Removal retains the buffer address,
but borrowed views keep their original captured length.
