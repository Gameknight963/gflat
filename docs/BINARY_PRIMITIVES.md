# Binary scalar buffers

`std::BinaryPrimitives` reads and writes signed and unsigned 16-, 32-, and 64-bit integers,
32-bit `float`, and 64-bit `double` in explicitly selected little- or big-endian byte order. The operations do not
depend on native alignment, host endianness, or the platform's C `long` width.

```csharp
using std;
byte[8] data;
bool written = BinaryPrimitives::TryWriteUInt32BigEndian(data, 8, 305419896u);
uint value = 0u;
bool read = BinaryPrimitives::TryReadUInt32BigEndian(data, 8, ref value);
```

Every method accepts a byte pointer and the available byte count. Reads return
false and set their output to zero if the count is shorter than the scalar.
Writes return false without touching storage in that case. Success consumes only
the scalar's width; excess bytes are untouched. No terminator is written.

Inputs must cover the stated readable or writable bytes. Unaligned addresses
are supported because access happens one byte at a time. All methods are
`nothrow`, allocate no storage, and need no allocator or exception runtime.
They do not serialize arbitrary object representations or pointer values.

Signed counterparts use `Int16`, `Int32`, and `Int64` in the same method names,
with `short`, `int`, and `long` values. They preserve the corresponding unsigned
bit pattern, using two's-complement interpretation on read. Successful reads
also support an output variable whose storage overlaps the input; all input
bytes are read before the result is stored. A failed read still writes zero
to its output, including when that output overlaps input.

## Floating-point values

`TryReadFloatLittleEndian` / `TryWriteFloatLittleEndian`, their big-endian
counterparts, and the matching `Double` forms use the same pointer/count
signatures with `float` or `double` values. They preserve the exact IEEE 754
binary32/binary64 representation, including negative zero, infinities, and all
NaN signs and payloads. No floating-point arithmetic or numeric conversion is performed.
Short reads produce positive zero; short writes leave bytes untouched.

Integer helpers assemble/disassemble the wire bits byte by byte. Reinterpretation
uses aligned local scalar storage, so the byte buffer still need not be aligned.
These methods require no separate `BitConverter.gf` dependency. A successful
read finishes loading the input before storing its output, including when that
output's storage overlaps input.

For a freestanding Windows x64 executable, LLVM still emits the `_fltused`
linkage marker for floating-point code. The CRT normally provides it; custom
startup can provide an integer definition when no CRT floating-point setup is
required, as [LLVM's maintainers explain](https://discourse.llvm.org/t/lld-link-fails-to-link-uefi-binary-because-of-undefined-symbol-fltused/52454).
The no-CRT test startup does this explicitly and verifies that only `ExitProcess`
is imported. `NoStdLib` and `NoBuiltins` do not supply a complete startup or
platform ABI implementation.
At O0 this marker can be required even when a caller only uses the integer
methods: the same source file also emits floating-point method definitions.

For stream equivalents, use `FileBinary` from the libc library. Buffer helpers
can instead be used with caller-owned arrays, network packets, or mapped data.
`FileBinary::ReadInt16LittleEndian` / `WriteInt16LittleEndian` and the matching
32-/64-bit and big-endian forms use the same signed interpretation. Like the
unsigned stream operations, they consume or write exactly the integer width;
short input throws `EndOfStreamException`, and closed or failing streams throw
`IOException`. They borrow the `File*` without closing it.
