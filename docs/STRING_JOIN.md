# Joining borrowed Strings

`Join` combines a span of Strings with a separator:

```csharp
using Standard::Text;
using Standard::Memory;

extension Span<immutable(String)> {
    public readonly String Join(readonly(char)* separator);
}
extension Span<immutable(String)> {
    public readonly String Join(readonly(char)* separator, nuint separatorCount);
}
```

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

Vector<String> fields = new();
fields.Add(s"one");
fields.Add(s"two");
String line = fields.AsImmutable().Join(", "); // one, two
```

| Input | Result |
| --- | --- |
| Empty span | Empty String |
| One element | Independent copy of that element |
| Multiple elements | Counted contents with separators between positions |

Empty elements still contribute separators. The C-string separator ends at NUL.
The counted form includes embedded NULs. Both overloads ignore the separator for
fewer than two elements.

Keep input Strings and separator bytes stable during the call. A separator may
borrow one of the input Strings. The returned result owns its contents.
Size overflow throws `Exception`, and allocation failure throws `BadAllocException`.

Source: `core/Text/string_join.gf`
