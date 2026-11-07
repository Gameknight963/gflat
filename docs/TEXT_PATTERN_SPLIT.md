# Splitting on an exact byte pattern

`TextPartsPattern` lazily splits borrowed counted text on a multi-byte separator.
Load its type source and `text_parts_pattern_span.gf` for readonly text spans,
or `string_split_pattern.gf` for `String` receivers. All belong to
`Standard::Text`.

```csharp
using Standard::Text;
using Standard::Memory;

int main()
{
    String input = s"one::two::::three::";
    var parts = input.Split("::", true);
    nuint count = 0;
    foreach (Span<readonly(char)> part in parts)
    {
        if (part.IsEmpty) return 1;
        count++;
    }
    if (count != 3) return 2;
    return 0;
}
```

`Split(separator, removeEmpty = false)` accepts a zero-terminated separator
or a counted `Span<readonly(char)>`. A pointer/count overload also accepts
embedded zero bytes. Matching compares exact bytes without case folding or
Unicode normalization. Matches are consumed left to right without overlap:
`"aaaaa"` split on `"aa"` yields two empty parts followed by `"a"`.

An empty separator leaves the input as one whole part. Empty input produces
one empty part unless `removeEmpty` is true. Adjacent and trailing separators
produce empty parts unless that option is enabled.

Both input and separator storage are borrowed. Keep them alive and stable
through enumeration. Returned spans borrow the input bytes and can outlive
the enumerator while that storage remains valid. A temporary String receiver
does not extend its lifetime to match a saved split view. Freestanding String
forms take `ref immutable(String)` to require existing storage.

Successful construction and enumeration do not allocate. Each enumerator has
its own position, and repeated `Current` reads return the same slice. Accessing
`Current` before the first successful move or after exhaustion throws.
