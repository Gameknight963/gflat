# Core utilities without a runtime

`Ascii`, `Utf8`, `Utf16`, `UnicodeTranscoding`, `Base64`, `Hex`, `PercentEncoding`, `BinaryPrimitives`, `Hashing`,
`BitOperations`, `CRC32`, `Adler32`, `Utf8Decoder`, `BinaryBufferReader`,
`BinaryBufferWriter`, `IntegerFormatting`, `ByteSearch`, `CString`, `JsonString`, `VarInt`, and the raw
byte operations in `Memory/Bytes.gf` can operate on caller-owned buffers
without libc, allocation, or the gflat exception runtime. Their buffer
and scalar functions are `nothrow`; malformed text and insufficient capacity are
reported through their return values. Caller pointers must still designate valid
memory for the specified counts.

The compiler now emits ordinary calls to known `nothrow` functions, including
calls through virtual/interface contracts carrying that guarantee. Function
pointers do not encode that promise and remain conservative. A `nothrow` function
still terminates if it actually encounters an unhandled exception; this does not
silently turn exceptions into return values.

## Verified executable

The native test
[`FreestandingCoreUtilitiesTests`](../gflat.Tests/FreestandingCoreUtilitiesTests.cs)
compiles an ordinary gflat `main` with explicitly selected utility sources,
`NoStdLib`, `NoBuiltins`, and internal function linkage. A minimal platform startup calls it
and exits through the operating system. It links with `-nostdlib`, without the
gflat native runtime objects or default C/C++ libraries.

Run it with:

```console
dotnet test gflat.Tests/gflat.Tests.csproj --filter FullyQualifiedName~FreestandingCoreUtilitiesTests
```

The test runs at both `-O0` and `-O2`, checks UTF-8 scalar and binary-text round
trips, unaligned integer byte order, streaming checksums, and a length-delimited
binary packet with bounded child cursors. Packet checks include explicit mixed
byte order, signed payloads, guard bytes, and unchanged positions on failure.
It also round-trips counted UTF-16 code units through UTF-8, preserving BMP and
supplementary scalars, embedded U+0000, U+FEFF, and a valid noncharacter. Both
conversion directions check capacity failures and malformed suffixes after valid
prefixes, verifying that output remains completely unchanged. UTF-16 counts are
host `ushort` units; no endian-byte-stream or BOM conversion is implied.
Decimal integer checks format signed 64-bit minimum and unsigned 64-bit maximum
into guarded caller buffers. Their values come from a separate assembly object
so LLVM cannot replace formatting with constant output and hide dependencies.
Both optimization levels link successfully without a libc `memcpy` helper.
Variable-length integer checks encode and decode runtime-populated 32-bit and
64-bit values, including their maxima. They also check truncated input,
overflow, noncanonical encodings, short output buffers, and unchanged output on
failure. See [VarInt](VARINT.md) for its canonical base-128 contract.
Counted byte searches check first and last overlapping matches, embedded zero
bytes, empty patterns, and missing patterns in periodic data. A separate assembly
routine populates the search input so LLVM cannot replace the searches with
constant answers at O2.
The same runtime-populated bytes exercise all six C-string helpers, including a
bounded scan whose terminator lies outside its capacity. `NoBuiltins` prevents
LLVM from replacing the unbounded length loops with external `strlen` calls.
Raw memory checks copy counted data, move overlapping regions in both directions,
fill guarded buffers, compare unsigned bytes, and verify zero-count operations.
Their bytes and move count also originate outside LLVM's module. `NoBuiltins`
keeps the fill loop from introducing an external `memset` dependency in this
test; the final executable import checks remain in force.
JSON string checks quote runtime-populated control bytes, verifying each escape
and both surrounding quotes without a null terminator. They also exercise
quotation-mark, backslash, and newline mutations, insufficient capacity, and a
malformed UTF-8 suffix, with unchanged output on both failures.
The test also inspects the final executable:

- Windows x64 imports exactly `KERNEL32.dll!ExitProcess`, with no CLR header.
- Linux x64 uses a syscall exit stub and checks that the ELF has no dynamic
  interpreter or dynamic section. The same test is included in the Linux CI job.

An earlier version of the demo, before the byte-order, percent-encoding, and
checksum checks were added, measured 10,752 bytes at `-O0` and 6,144 bytes at
`-O2` on Windows. These are illustrative measurements, not size guarantees.

Floating-point binary checks preserve signed signaling-NaN bit patterns through
unaligned buffer cursors in mixed byte order. The values originate in the same
opaque assembly input. No floating-point arithmetic or text formatting occurs.

On Windows x64, LLVM also emits the `_fltused` ABI marker for floating-point
definitions. Even an integer-only caller that includes `BinaryPrimitives.gf`
can need this marker at O0 because unused floating-point definitions remain in
the object. A focused test compiles that minimal integer-only client at O0 and
inspects its COFF symbol table for the undefined marker.

The platform-owned test startup supplies `@_fltused = global i32 0`; this is a
data definition, not a call into the CRT. The executable still imports only
`ExitProcess`. [LLVM's maintainers describe this standalone startup responsibility](https://discourse.llvm.org/t/lld-link-fails-to-link-uefi-binary-because-of-undefined-symbol-fltused/52454).
Neither `NoStdLib` nor `NoBuiltins` supplies startup, floating-point environment
initialization, or other platform ABI definitions. `BitConverter.gf` remains
separately tested and is not a dependency of these byte-order helpers.

## Limits

`NoStdLib` selects sources; it does not itself replace platform startup or disable
all default native linker libraries. The test supplies those details explicitly.
`CompilationOptions.NoBuiltins` separately disables LLVM's recognition and
introduction of known libc/libm builtins in compiler-generated functions. It is
false by default, independent of `NoStdLib`. Generated methods, helpers, lambdas,
interface thunks, and inline LLVM body wrappers receive the `"no-builtins"`
function attribute; arbitrary `__llvm_module` definitions retain the attributes
their author supplied. No general optimization pass is disabled.

This optimizer policy does not prohibit explicit libc calls, nor does it prevent
existing memory intrinsics from lowering to external `memcpy` or `memset` calls.
Platform ABI helpers, stack probes, and arithmetic support are also separate.
Final executable inspection remains the evidence for the particular tested
program's runtime independence. LLVM's
[TargetLibraryInfo implementation](https://llvm.org/doxygen/TargetLibraryInfo_8h_source.html)
defines the attribute's library-availability behavior.
The compiler still needs its existing build tools; runtime independence does not
remove compiler installation requirements.

Other core facilities have additional requirements. Owning containers need
`Memory::Allocate`/`Free`; throwing operations need exception support. `PseudoRandom`
currently includes throwing bounded methods in the same type, so selecting that
source also requires the relevant exception and allocation definitions. Libc file,
console, and floating-text APIs retain their documented host dependencies.
