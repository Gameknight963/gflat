# Dotted numeric versions

`Standard::Version` stores `major.minor[.build[.revision]]` as two through four
nonnegative 32-bit components. Use it for numeric protocol or file revisions.

```csharp
using Standard;

int main()
{
    Version original = Version::Parse("001.02");
    Version extended = new(1, 2, 0);
    if (original.Major != 1 || original.Minor != 2 || original.Build != -1) return 1;
    if (original.CompareTo(&extended) >= 0 || original.Equals(&extended)) return 2;
    char[43] buffer;
    nuint written = 0;
    if (!original.TryFormat(buffer, 43, ref written) || written != 3) return 3;
    if (buffer[0] != '1' || buffer[1] != '.' || buffer[2] != '2') return 4;
    return 0;
}
```

## Components and ordering

Default construction gives `0.0`. `Version(major, minor, build=-1, revision=-1)`
uses `-1` for absent trailing components. Revision requires a build component.
Other invalid tuples throw `Exception("Invalid version components")`.

`Major`, `Minor`, `Build`, and `Revision` expose these component values.
Comparison is lexicographic, with missing parts before zero. Thus `1.2`,
`1.2.0`, and `1.2.0.0` are distinct and increasingly ordered. Equality and
hashing preserve those distinctions.

## Parsing

Parse and TryParse accept C strings or counted input with two through four
ASCII decimal components. Signs, whitespace, empty components, suffixes, and
values above `int` maximum are rejected. Leading zeroes are accepted, even
when they make input longer than the maximum canonical output.

TryParse returns false and resets its output to `0.0`. Parse throws
`Exception("Invalid version")`. Reads complete before output assignment, so
properly aligned result storage can overlap readable input.

## Formatting

`TryFormat(output, capacity, ref written)` writes canonical digits and dots,
retaining the specified component count and dropping leading zeroes. Output
is counted and requires at most 43 bytes. Insufficient capacity returns false,
sets `written` to zero, and preserves the buffer.

Formatting stages its bytes, allowing overlap with sufficiently backed receiver
storage. Keep `written` separate from receiver and output. `Format(writer)`
sends the same representation in one write.

Version implements `IEquatable<Version>`, `IComparable<Version>`, `IHashable`,
and `IFormattable`.
