# Borrowed UTF-16 scalar iteration

`std/core/Utf16Scalars.gf` provides `std::Utf16Scalars`, a repeatable borrowed view
of counted host `ushort` code units. Include `Utf16.gf`, `Collections/IEnumerable.gf`,
`Collections/IEnumerator.gf`, and `Queries/Enumerable.gf` alongside the view source.

```csharp
ushort[4] units = [(ushort)65, (ushort)0, (ushort)0xd83d, (ushort)0xde00];
std::Utf16Scalars scalars = new(units, 4);
foreach (uint scalar in scalars) {
    // U+0041, U+0000, U+1F600
}
```

`CodeUnitLength` is a `nothrow` getter reporting physical input length, not scalar count. Input is already
host code-unit values, not an endian-specific byte buffer. There is no BOM
processing, terminator handling, normalization, or grapheme-cluster grouping.
Embedded U+0000, U+FEFF, and valid noncharacters are ordinary scalars.

The default view and `new(null, 0)` are empty. Null storage with a nonzero length
throws at construction. Non-null storage must remain readable for the supplied
count throughout use of the view and all its iterators. Creating/copying a view,
obtaining an iterator, and valid enumeration allocate nothing. They neither copy
nor own the input; do not release its storage or let it go out of scope while a
view or iterator still uses it. Early termination does not release caller data.

The nested `Enumerator` implements `IEnumerator<uint>`, and the view implements
`IEnumerable<uint, Utf16Scalars::Enumerator>`. Each `GetEnumerator()` starts at
the beginning. Copying an iterator copies its position and cached current scalar;
copies subsequently progress independently. Changes to unread borrowed input are
visible on later reads. A successful `Current` is cached until the next
`MoveNext()` and does not change when the underlying units change.

`AsEnumerable()` exposes the concrete lazy-query wrapper for `Where`, `Select`,
`Take`, and aggregates when their collection source files are included. Both it
and `GetEnumerator()` accept mutable, shallow readonly, or immutable views.
The adapter copies only borrowed pointer/count metadata; keep the same backing
units live for every later enumeration. Repeating a query starts again from
the beginning and observes subsequent changes to backing units. An early
`Take` can stop before malformed input, preserving the iterator's existing lazy
validation rules. See [lazy queries](../std/core/Queries/README.md).

Like `Utf8Scalars`, this iterator validates lazily and throws on malformed input
when `MoveNext()` reaches it. It does not replace isolated/truncated/incorrect
surrogate sequences. A valid prefix can be read without inspecting its suffix,
and breaking early does not validate the remaining input. These throwing paths
require the ordinary exception runtime; use `Utf16::TryDecode` or `Utf16Decoder`
for a `nothrow` result-based API.

`Current` throws before the first successful move, after reaching the end, and
after a failed move. Repeated end moves return false. A decoding failure leaves
the position unchanged, so retries encounter the same malformed sequence unless
the caller repairs the borrowed storage. Iteration does not roll back previous
successfully consumed scalars.
