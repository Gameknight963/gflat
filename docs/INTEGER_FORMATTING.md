# Decimal integer formatting into caller buffers

`std/core/IntegerFormatting.gf` supplies `std::IntegerFormatting::TryFormat`
overloads for `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`nint`, and `nuint`. The functions are `nothrow` and require no String, libc,
allocation, exception runtime, or writer interface.

```csharp
char[20] buffer;
nuint written = 0;
bool success = std::IntegerFormatting::TryFormat(-123, buffer, 20, ref written);
// success == true; the first written bytes are "-123".
```

Output is invariant ASCII decimal: no grouping, leading plus sign, padding, or
leading zeros. Negative values have a leading minus sign; zero is one byte.
Signed minimum values are supported without signed negation overflow. Pointer
integers use the target's pointer width.

The result is counted bytes with **no null terminator**. A 20-byte buffer is
sufficient for any supported integer. Callers needing a C string must reserve
another byte and append the terminator themselves after success.

Each call first counts decimal digits, then writes directly backwards into the
output after checking capacity. No temporary-buffer copy or libc helper is needed.
Insufficient capacity returns false, sets `written` to zero, and leaves every
output byte untouched. A zero capacity does not access output storage. On success
exactly `written` bytes change; bytes outside that range remain unchanged.
The caller supplies writable storage for the stated capacity and must keep
`written` storage disjoint from the output buffer.

The existing writer-based `std::Format` functions remain independently usable
with their existing source dependencies. This optional source is intended for
caller-buffer and freestanding code that does not need writer dispatch.
