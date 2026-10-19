# Binary integer buffers

`std::BinaryPrimitives` reads and writes unsigned 16-, 32-, and 64-bit integers
in explicitly selected little- or big-endian byte order. The operations do not
depend on native alignment, host endianness, or the platform's C `long` width.

```csharp
using std;
byte[8] data;
bool written = BinaryPrimitives::TryWriteUInt32BigEndian(data, 8, 305419896u);
uint value = 0u;
bool read = BinaryPrimitives::TryReadUInt32BigEndian(data, 8, ref value);
```

Every method accepts a byte pointer and the available byte count. Reads return
false and set their output to zero if the count is shorter than the integer.
Writes return false without touching storage in that case. Success consumes only
the integer's width; excess bytes are untouched. No terminator is written.

Inputs must cover the stated readable or writable bytes. Unaligned addresses
are supported because access happens one byte at a time. All methods are
`nothrow`, allocate no storage, and need no allocator or exception runtime.
They do not copy object representations or serialize pointer values.

For stream equivalents, use `FileBinary` from the libc library. Buffer helpers
can instead be used with caller-owned arrays, network packets, or mapped data.
