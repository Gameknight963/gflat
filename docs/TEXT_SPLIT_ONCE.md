# Split a borrowed key and value

`Text/text_split_once_span.gf` adds one operation to `Span<readonly(char)>`:

```csharp
using std::Memory;
bool TrySplitOnce(this Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after);
```

It scans for the first matching byte and returns borrowed views before and after
that separator. Further separators remain part of `after`, making it useful for
key/value settings and command arguments.

```csharp
using std::Text;
using std; using std::Memory;
String setting = s"mode=fast=checked";
Span<readonly(char)> input = setting.AsReadOnly();
Span<readonly(char)> key = new();
Span<readonly(char)> value = new();
bool found = input.TrySplitOnce('=', ref key, ref value);
// found is true; key is "mode" and value is "fast=checked".
```

The separator is excluded from both views. A separator at either end produces
an empty side; a one-byte input containing the separator produces two empty
views and returns true. Missing separators, including in empty input, return
false and reset **both** outputs to canonical empty spans (length zero, null
Data). Embedded nulls and high bytes are ordinary counted data. This operation
does not decode Unicode or inspect a terminator.

The two output references must designate distinct span variables. The receiver
is passed by value, so either output may reuse the receiver variable's metadata:
`input.TrySplitOnce('=', ref key, ref input)` keeps the remainder in `input`.
The source bytes are never changed or copied, and the operation allocates nothing.
It uses the existing checked Span indexer/slicing operations and does not add a
`nothrow` guarantee to those APIs.

Keep the source owner alive and its storage stable while using either result.
The views do not retain ownership or extend a lifetime. Changes to backing bytes
through another valid alias are visible in the returned spans. Character writes
through the returned views remain forbidden. Mutable spans can use
`text.AsReadOnly().TrySplitOnce(...)`.

For selective source use, include `Text/text_split_once_span.gf`, `Memory/Span.gf`, and the
ordinary span/enumeration dependencies. The helper itself needs no String,
CString, ByteSearch, or allocator implementation. The owning String example also
uses the optional `string_span.gf` view convenience.

See [all-delimiter splitting and ASCII trimming](TEXT_PARTS.md) for enumerating
every field rather than preserving the whole remainder.

Optional [multi-byte separator overloads](TEXT_SPLIT_PATTERN.md) accept counted
bytes, a C string, or a separator span. They reuse `ByteSearch` and have their own
source file, leaving the single-character helper's dependencies unchanged.

`Text/text_split_last_once_span.gf` provides the matching `TrySplitLastOnce`
character overload. It searches from the end, preserving earlier separators in
`before`. For example, splitting `"a=b=c"` at `'='` gives `"a=b"` and `"c"`.
It has the same borrowing, failure, empty-side, and output-alias contracts as
`TrySplitOnce`, and needs only the ordinary Span dependencies. Optional
[byte-sequence overloads](TEXT_SPLIT_PATTERN.md) also support last-match splitting.
