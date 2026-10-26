# Convert a value to an owning String

With `using std;`, `ToString()` offers the same textual representation
as the value's existing `Format` operation, collected into a new owning `std::String`.
It introduces no separate formatting policy, encoding conversion, or ToCString
helper. A returned String remains owned by its caller and must stay alive while
any pointer borrowed from it is used.
For a borrowed C string, use `String::From(text)`.

```csharp
using std; using std::Time;

int main()
{
    DateTime moment = DateTime::Parse("2026-10-24T12:34:56");
    String dateText = moment.ToString();
    int count = 42;
    String countText = count.ToString();
    if (!dateText.Equals("2026-10-24T12:34:56") || !countText.Equals("42")) return 1;
    return 0;
}
```

The generic extension is
String ToString<T : IFormattable>(this ref immutable(T) value).
It borrows an existing value and calls its immutable Format once with the result
String as the writer. No source copy is required, so noncopyable formatter values
can use it. Named mutable values can be borrowed through the immutable view;
this does not freeze other aliases. Callbacks must obey their ordinary formatting
and lifetime contracts. No new IStringConvertable implementation is added to the
source type merely because an extension is available.

A ref receiver follows the language's existing storage restrictions, including
its restriction on managed storage. Locals, ref parameters, and valid borrowed
or ref-returned storage can supply the receiver. A temporary/value property does
not acquire storage merely to call this extension. For example, retain a
DateTime::Parse(...) result in a local before calling its extension ToString().
Instance ToString methods still take priority. String, Rune and BigInteger keep
their existing instance behavior, including their normal temporary-call support.

Primitive by-value extensions cover byte, sbyte, short, ushort, int, uint, long,
ulong, nint, nuint, bool and char. Their existing Format policies apply: decimal
integers, lowercase true/false, and one counted byte for char. A NUL character
therefore produces a String of Length 1 containing NUL; it is not an empty String.
The separate extralong extension uses ExtraLongFormatting's full-width decimal
format. Primitive receivers are copied values, so primitive temporaries are not
restricted by the generic ref-receiver storage rule.

Float and double conversions live alongside libc formatting. They use the
existing snprintf policy, including the application's C locale and current
9/17 significant-digit precision. This addition promises neither a new invariant
float syntax nor a new NaN/infinity policy. Core-only selection does not gain a
libc formatting dependency from these optional conversions.

Each extension constructs an empty String, formats into it, and moves it to the
caller. It creates no additional sequence buffer or managed allocation. String
can grow more than once when a custom formatter writes several chunks. Allocation
or formatter exceptions propagate; the partial result is cleaned up by ordinary
value lifetime rules. An existing assignment target in the caller is replaced
only after a successful return, apart from callback side effects through aliases.
An empty formatter returns the ordinary empty String without result allocation.

Selective source files and dependencies:

- core/FormattableString.gf: String and IFormattable/Text contracts; custom formatter dependencies remain its own.
- core/PrimitiveString.gf: String plus existing core/Formatting.gf.
- core/ExtraLongString.gf: String, ExtraLongFormatting.gf and IntegerFormatting.gf.
- libc/FloatingString.gf: String plus existing libc/Formatting.gf and its snprintf linkage.

String itself retains its normal interface, hashing and ByteSearch dependencies.
These are convenience source files; they do not modify interfaces or compiler rules.
