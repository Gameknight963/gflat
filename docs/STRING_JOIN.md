# Joining borrowed Strings

The optional `std/core/Text/string_join.gf` source joins a borrowed span of String
owners into one independent owning String. It adds these nongeneric extensions:

```csharp
using std::Memory;
String Join(this Span<immutable(String)> values, readonly(char)* separator);
String Join(this Span<immutable(String)> values, readonly(char)* separator, nuint separatorCount);
```

For example:

```csharp
using std; using std::Collections;
Vector<String> fields = new();
fields.Add(s"one");
fields.Add(s"two");
String line = fields.AsImmutable().Join(", "); // one, two
```

The source span copies only its pointer and count; its String elements are never
copied or consumed. Both overloads preserve every byte of every element, including
embedded zeros and non-UTF-8 bytes. The C-string separator ends at its first zero;
the counted overload preserves all separator bytes, including zeros.

An empty span returns an empty String. A singleton returns an independent copy of
its element. Both cases ignore the separator completely: even the C-string
overload does not scan it. Empty elements still contribute separators between
their positions. An empty result needs no allocation.

The full result length, including separator multiplication and the final null
terminator requirement, is checked before allocating output storage. Overflow
throws `Exception`. A nonempty result reserves its exact payload once; allocation
failure throws `BadAllocException`. Success always returns a terminated String,
whose `Length` excludes the terminator. Sources and separator remain unchanged
after either success or failure.

The span's owner storage, every source String, and the separator bytes must stay
alive and unchanged throughout the call. Separator bytes can borrow one of the
source strings: only the fresh result is modified. After success the result no
longer borrows any input. These are ordinary raw lifetime preconditions, without
compiler lifetime tracking or concurrent mutation support.

Explicit source selections need `string_join.gf`, String and Span with their
ordinary dependencies. This optional source adds no Span dependency to
`Text/String.gf` itself. The complete standard library includes it automatically.
