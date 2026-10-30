# Dotted numeric versions

Standard::Version is an ordinary value with two through four nonnegative signed
32-bit integer components: major.minor[.build[.revision]]. It is not SemVer:
there are no prerelease labels, build metadata, version ranges, or coercions.
It can describe numeric protocol or file-format revisions.

The default value is 0.0. Construct Version(major, minor, build=-1, revision=-1);
-1 denotes an absent trailing optional component. Required components must be
nonnegative, optional components cannot be below -1, and revision cannot be
present when build is absent. Invalid tuples throw Exception("Invalid version components").
Immutable Major, Minor, Build, and Revision getters expose these exact values.

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

Ordering is lexicographic by the four component values; missing parts sort
before zero. Thus 1.2, 1.2.0, and 1.2.0.0 are distinct, increasingly ordered
values. Comparisons use relational checks rather than potentially overflowing
subtraction. Equality and hashing include missing-component distinctions. Hash
codes need not match .NET and are not a serialized protocol.

TryParse/Parse each accept either a counted readonly(char)* plus nuint length,
or a null-terminated readonly(char)*. The entire supplied input must be two to
four nonempty ASCII decimal components separated by single dots. Whitespace,
signs, suffixes, counted embedded NUL, and component values beyond int maximum
are rejected. Leading zeroes are accepted and normalized, including inputs
longer than 43 bytes. Only canonical output has a 43-byte maximum. Counted
input need not have a terminator; C-string input requires one.

TryParse writes the default 0.0 result on failure and returns false. Its input
reads precede the final output assignment, so a suitably aligned output value
may overlap readable input storage with enough backing space. Parse throws
Exception("Invalid version"). Result pointers must designate valid writable
Version storage; there is no null-output shortcut.

TryFormat(char* output, nuint capacity, ref nuint written) writes canonical
ASCII digits and separators, without a NUL. It retains the specified number of
components and removes leading zeroes. Failure leaves bytes unchanged and sets
written to zero. Staging allows receiver/output overlap when backed by enough
storage; written must be separate from both. Format(ITextWriter*) emits one
counted write and propagates writer errors normally. There is no String
dependency or allocation on valid parse/format/constructor paths; throwing
errors and the selected writer may allocate.

The value implements IEquatable<Version>, IComparable<Version>, IHashable and
IFormattable, so it composes with ordinary sets, maps, comparisons and writers.

