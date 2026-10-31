# Convert a value to an owning String

With `using Standard::Text;`, `ToString()` offers the same textual representation
as the value's existing `Format` operation, collected into a new owning `Standard::Text::String`.
A returned String remains owned by its caller and must stay alive while
any pointer borrowed from it is used.
For a borrowed C string, use `String::From(text)`.

```csharp
using Standard::Text;
using Standard; using Standard::Time;

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
`extension<T : IFormattable> T { public immutable String ToString(); }`.
It borrows an existing value and calls its immutable Format once with the result
String as the writer. No source copy is required, so noncopyable formatter values
can use it. Named mutable values can be borrowed through the immutable view.
This does not freeze other aliases. Callbacks must obey their ordinary formatting
and lifetime contracts.

Extension methods borrow locals, references, and managed pointees. Temporary
receivers remain alive through the call, so DateTime::Parse(...).ToString()
does not require an intermediate local. Instance ToString methods still take
priority. No receiver copy or ownership transfer is introduced by member syntax.

Primitive extensions cover byte, sbyte, short, ushort, int, uint, long,
ulong, nint, nuint, bool and char. Their existing Format policies apply: decimal
integers, lowercase true/false, and one counted byte for char. A NUL character
therefore produces a String of Length 1 containing NUL. It is not an empty String.
The separate extralong extension uses ExtraLongFormatting's full-width decimal
format. Primitive receivers are borrowed, including temporary values.

Float and double conversions live alongside libc formatting. They use the
`snprintf` policy, including the application's C locale and 9/17 significant-digit
precision. Include the floating conversion sources when using these overloads.

Each extension constructs an empty String, formats into it, and moves it to the
caller. It creates no additional sequence buffer or managed allocation. String
can grow more than once when a custom formatter writes several chunks. Allocation
or formatter exceptions propagate. The partial result is cleaned up by ordinary
value lifetime rules. An existing assignment target in the caller is replaced
only after a successful return, apart from callback side effects through aliases.
An empty formatter returns the ordinary empty String without result allocation.

Selective source files and dependencies:

- core/Text/formattable_string.gf: String and IFormattable/Text contracts. Custom formatter dependencies remain its own.
- core/Text/primitive_string.gf: String plus existing core/primitive_format.gf
- core/Text/extra_long_string.gf: String, extra_long_format.gf and Text/IntegerFormatting.gf
- libc/Text/floating_string.gf: String plus existing libc/floating_format.gf and its snprintf linkage

String itself retains its normal interface, hashing and ByteSearch dependencies.
