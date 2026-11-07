# Formatting selected version fields

`Version.TryFormat(output, capacity, ref written, fieldCount)` writes a selected
number of leading numeric components. Field counts from zero through the
number of available components are valid. Zero produces empty output, one
writes the major component, and larger counts add dotted components.

```csharp
using Standard;
using Standard::Text;

int main()
{
    Version version = new(3, 12, 7, 65538);
    String shortName = version.ToString(2);
    if (!shortName.Equals("3.12")) return 1;
    if (version.MajorRevision != 1 || version.MinorRevision != 2) return 2;
    char[43] output;
    nuint written = 0;
    if (!version.TryFormat(output, 43, ref written, 3) || written != 6) return 3;
    return 0;
}
```

Load `Standard/core/Text/version_string.gf` for the owning `ToString(fieldCount)`
extension in `Standard::Text`. Existing general `ToString()` conversion still
formats all available fields. `Version.Format(writer, fieldCount)` writes the
same selected representation to an `ITextWriter`.

`TryFormat` is `nothrow` and returns false for an invalid or unavailable field
count, or insufficient capacity. It sets `written` to zero and leaves the
buffer unchanged on failure. Success writes counted bytes without a terminator.
Formatting stages the result before writing, so sufficiently backed receiver
and output overlap is supported. The `written` reference must be separate.

`ToString(fieldCount)` and `Format(writer, fieldCount)` throw for an invalid
or unavailable count. Validation precedes allocation or writer calls. Writer
failures can leave a partial result according to the writer's contract.

`MajorRevision` and `MinorRevision` expose the signed 16-bit upper and lower
halves of `Revision`. A missing revision gives -1 for both. The low half can
be negative even when the full revision is nonnegative. Both properties are
`nothrow` and do not allocate.
