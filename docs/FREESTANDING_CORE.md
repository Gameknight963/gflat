# Core utilities without a runtime

`Ascii`, `Utf8`, `Utf16`, `UnicodeTranscoding`, `Base64`, `Hex`, `PercentEncoding`, `BinaryPrimitives`, `Hashing`,
`BitOperations`, `CRC32`, `Adler32`, `Utf8Decoder`, `BinaryBufferReader`,
`BinaryBufferWriter`, `IntegerFormatting`, and `ByteSearch` can operate on caller-owned buffers
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
`NoStdLib`, and internal function linkage. A minimal platform startup calls it
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
Counted byte searches check first and last overlapping matches, embedded zero
bytes, empty patterns, and missing patterns in periodic data. A separate assembly
routine populates the search input so LLVM cannot replace the searches with
constant answers at O2.
The test also inspects the final executable:

- Windows x64 imports exactly `KERNEL32.dll!ExitProcess`, with no CLR header.
- Linux x64 uses a syscall exit stub and checks that the ELF has no dynamic
  interpreter or dynamic section. The same test is included in the Linux CI job.

An earlier version of the demo, before the byte-order, percent-encoding, and
checksum checks were added, measured 10,752 bytes at `-O0` and 6,144 bytes at
`-O2` on Windows. These are illustrative measurements, not size guarantees.

`BitConverter` remains separately tested rather than included in this integer
and text executable. Its source has no std dependency, but actual Clang22
Windows x64 objects at both O0 and O2 reference the `_fltused` ABI marker.
This test deliberately supplies only process startup and the operating-system
exit call; it does not add floating-point ABI definitions.

## Limits

`NoStdLib` selects sources; it does not itself replace platform startup or disable
all default native linker libraries. The test supplies those details explicitly.
The compiler still needs its existing build tools; runtime independence does not
remove compiler installation requirements.

Other core facilities have additional requirements. Owning containers need
`Memory::Allocate`/`Free`; throwing operations need exception support. `PseudoRandom`
currently includes throwing bounded methods in the same type, so selecting that
source also requires the relevant exception and allocation definitions. Libc file,
console, and floating-text APIs retain their documented host dependencies.
