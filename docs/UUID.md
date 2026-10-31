# UUID values

`Standard::Uuid` stores an opaque 128-bit identifier in two private `ulong` fields.
Default construction is the Nil UUID (all zero bits). The numeric constructor
`Uuid(ulong upper, ulong lower)` and immutable `High`/`Low` getters use canonical
network-order halves: `00112233-4455-6677-8899-aabbccddeeff` has
`High == 0x0011223344556677ul` and `Low == 0x8899aabbccddeefful`.
This describes numeric significance, not the native byte layout of a struct or
the Windows GUID ABI. `IsNil` reports whether both halves are zero.

```csharp
using Standard::Text;
using Standard;
Uuid id = Uuid::Parse("00112233-4455-6677-8899-aabbccddeeff");
String label = s$"id={id}";
```

## Parsing and formatting

```csharp
static bool TryParse(readonly(char)* text, nuint count, Uuid* result) nothrow;
static bool TryParse(readonly(char)* text, Uuid* result) nothrow;
static Uuid Parse(readonly(char)* text, nuint count);
static Uuid Parse(readonly(char)* text);
immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow;
immutable void Format(ITextWriter* output);
```

Parsing accepts exactly 36 bytes in `8-4-4-4-12` hexadecimal groups separated by
hyphens. ASCII hex digits may use either letter case. Whitespace, braces, URN
prefixes, missing hyphens, embedded nulls, and other bytes are invalid. Every bit
pattern is accepted, including Nil, Max, and values with unrecognized version or
variant bits. Parsing identifies a value; it does not establish its origin or
uniqueness.

Counted parsing reads only the specified bytes. A count other than 36 fails
without reading the input. The C-string form scans at most 37 bytes to check the
terminator after the canonical form. `TryParse` returns false and writes Nil on
failure; `Parse` throws `Exception`. Parsing completes its reads before committing
a successful result, so suitably aligned writable input may overlap the result.
A failure's Nil store also changes overlapping input bytes.

For both `TryParse` overloads and `TryFromBytes`, `result` must always point to
non-null, properly aligned, writable storage for one `Uuid`, even on failure.
The previous value is not read. This permits caller-managed raw storage as well
as an initialized local. An ordinary local must still be initialized before its
address is passed, for example `Uuid result = new();`; these pointer parameters
do not provide an `out` initialization contract.

`TryFormat` emits exactly 36 lowercase bytes and no terminator. Capacity below 36
returns false, sets `written` to zero, and leaves output unchanged. The written
count must be separate from both the output region and receiver storage. The
numeric halves are captured before writing, so an output region large enough
for all 36 bytes may overlap the receiver's storage. `Format` implements
`IFormattable` with one 36-byte stack buffer and one `Write` call. Writer failures
propagate normally and may leave partial destination output. No String is
required by these value operations; interpolation can format directly into its
own destination.

## Canonical bytes

`TryFromBytes(data, count, &result)` accepts exactly 16 bytes, placing the first
eight in `High` and the remaining eight in `Low`, most significant byte first.
For example, bytes `00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff` represent
`00112233-4455-6677-8899-aabbccddeeff`. This is an explicit serialized order,
independent of native struct layout.

A count other than 16 returns false and resets the result to Nil without reading
input. `FromBytes(data, count)` returns the same value or throws `Exception`.
Both read the complete input before committing the result, permitting suitably
aligned result storage to overlap input.

`TryWriteBytes(output, capacity, ref written)` writes those 16 canonical bytes
without allocating. Capacity below 16 returns false, sets `written` to zero,
and leaves output unchanged. Success writes exactly 16 bytes and leaves any
remaining capacity untouched. All bytes are staged before output is changed,
so output may overlap a receiver backed by at least 16 writable bytes. The
written count must be separate from output and receiver storage. Both `Try`
operations are `nothrow` and retain no pointers.

## Identity and ordering

`Uuid` implements `IEquatable<Uuid>`, `IComparable<Uuid>`, and `IHashable`.
Equality compares all 128 bits. Ordering compares the unsigned high half, then
the unsigned low half, matching canonical/network-byte lexicographic ordering.
This is identifier ordering, not a timestamp interpretation of UUID versions.
It can be used as a `HashMap`/`HashSet` key or with sorted containers.

Hashing mixes all 16 canonical bytes with the existing `Hashing` functions.
Hashes are ordinary collection hashes, not persistent identifiers or
cryptographic authentication. Constructors, getters, comparisons, hashing, and
Try operations allocate nothing and are `nothrow` under their pointer-storage
preconditions. Buffers remain caller-owned; no pointers are retained.

For selective source use, include `Uuid.gf`, `ITextWriter.gf`, `IFormattable.gf`, `IEquatable.gf`,
`IComparable.gf`, `IHashable.gf`, and `Hashing/primitive_hashing.gf`. Throwing Parse and FromBytes
methods require the ordinary Exception/runtime contracts. This API generates no identifiers and
uses no clock, operating-system generator, or random source.

The representation and canonical hexadecimal form follow
[RFC 9562, section 4](https://www.rfc-editor.org/rfc/rfc9562.html#section-4).

## Command-line example

This program checks one identifier and prints its canonical lowercase form.
It uses the standard project library and does not construct an owning String:

```csharp
using Standard::Text;
using Standard;

int main(int argc, char** argv)
{
    if (argc != 2) {
        Console::Error.WriteLine("Usage: uuid-check <identifier>");
        return 2;
    }
    Uuid id = new();
    if (!Uuid::TryParse(argv[1], &id)) {
        Console::Error.WriteLine("Expected a UUID in 8-4-4-4-12 hexadecimal groups");
        return 2;
    }
    id.Format(Console::Out);
    Console::WriteLine();
    Console::Flush();
    return 0;
}
```

## Version 4 generation with an explicit source

`Standard::UuidGenerator` creates version 4 UUIDs from a caller-selected random-byte
callback. It has no default source, global state, or operating-system dependency.
The existing `Uuid` value and parsing APIs do not require the generator source.

```csharp
static bool TryCreateVersion4(bool(byte*, nuint)* fill, ref Uuid result);
static Uuid CreateVersion4(bool(byte*, nuint)* fill);
```

Each call invokes `fill` exactly once with a writable 16-byte stack buffer and a
count of 16. The callback must fill all 16 bytes before returning true. That buffer
is valid only during the callback: do not retain its address or access it after
returning. The generator retains no callback or buffer pointers and allocates no
heap storage on success; the callback may perform its own work or allocate.

On success, canonical byte 6 receives the version nibble 4, and canonical byte 8
receives the variant bits 10. The other 122 bits retain the supplied random bits.
The result is an ordinary `Uuid` in the same canonical byte order as
`Uuid::TryFromBytes`. Randomness quality comes from the chosen callback; masking
these six bits does not make a weak source secure or guarantee uniqueness.

If `fill` returns false, `TryCreateVersion4` discards any partial bytes, writes Nil
to `result`, and returns false. `CreateVersion4` throws `Exception` instead.
Exceptions thrown by the callback propagate through both methods. The generator
itself does not assign the result before the callback completes, so a callback
exception leaves the caller's result unchanged unless the callback changes it
through another alias. Neither method is declared `nothrow`.

Optional platform libraries already expose compatible callbacks. After adding the
appropriate project reference, a Windows caller can write:

```csharp
Uuid id = UuidGenerator::CreateVersion4(Windows::SystemRandom::TryFill);
```

The Linux counterpart is `Linux::SystemRandom::TryFill`. Reference only the
platform library appropriate to the application; no platform detection or fallback
is performed. Existing platform requirements and native linkage remain those of
the selected library. A custom callback needs neither optional library.

For selective source use, add `UuidGenerator.gf` alongside `Uuid.gf` and its
existing contract sources. The throwing convenience method uses the ordinary
Exception/runtime contracts; the Try method still permits callback exceptions.
Version 4 layout follows [RFC 9562, section 5.4](https://www.rfc-editor.org/rfc/rfc9562.html#section-5.4).
