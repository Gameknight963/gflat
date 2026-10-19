# Strict UTF-16 code-unit helpers

`std/core/Utf16.gf` supplies `std::Utf16`, an allocation-free, `nothrow` core
codec with no String, libc, GC, or exception-runtime dependency.

Inputs and outputs are **host `ushort` code-unit values**. Lengths, capacities,
consumed counts, and written counts all count code units, not bytes. The codec
does not parse an endian-specific byte stream, swap byte order, remove a BOM,
append a terminator, normalize text, or count grapheme clusters. U+FEFF is an
ordinary scalar, and embedded U+0000 participates in counted input.

| Function | Result |
| --- | --- |
| `IsValidScalar(uint scalar)` | Excludes surrogate values and values above U+10FFFF. |
| `EncodedLength(uint scalar)` | 1 or 2 code units; zero for invalid scalars. |
| `TryDecode(input, count, scalar, consumed)` | Decodes the first scalar; remaining input is not validated. |
| `TryEncode(scalar, output, capacity, written)` | Writes one scalar without a terminator. |
| `TryCount(input, count, scalarCount)` | Validates all input and counts Unicode scalars. |
| `IsValid(input, count)` | Validates all input. |

The input pointer is `readonly(ushort)*`, encoded output is `ushort*`, scalar
output is `uint*`, and count outputs are `nuint*`. The caller supplies valid,
appropriately aligned storage. A zero input count performs no reads through the
input pointer; empty input is valid and contains zero scalars. Decoding an empty
input returns false. Output metadata pointers must always be writable.

High surrogates U+D800-U+DBFF must be followed by a low surrogate U+DC00-U+DFFF.
Isolated low surrogates, truncated pairs, and incorrectly paired surrogates fail.
No replacement character is emitted. Noncharacters such as U+FFFF are valid
Unicode scalar values and are accepted.

On decode failure, scalar and consumed outputs are both zero. These two outputs
must be disjoint from each other, but either may overlap input storage: stores
occur only after all units required to decide the result have been read.
`TryCount` likewise permits its count output to overlap input, storing only after
the required scan; validation failure sets it to zero.

Encoding invalid scalars or with insufficient capacity returns false, sets
written to zero, and leaves encoded output untouched. The written-count storage
must be disjoint from encoded output. A successful supplementary scalar writes
its complete surrogate pair; failures never leave a partially encoded pair.
