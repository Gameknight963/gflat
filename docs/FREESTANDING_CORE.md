# Core utilities without a runtime

`Ascii`, `Utf8`, `Base64`, `Hex`, `PercentEncoding`, `BinaryPrimitives`, `Hashing`,
`BitOperations`, `CRC32`, and `Adler32` can operate on caller-owned buffers
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
trips, unaligned integer byte order, streaming checksums, and the final executable:

- Windows x64 imports exactly `KERNEL32.dll!ExitProcess`, with no CLR header.
- Linux x64 uses a syscall exit stub and checks that the ELF has no dynamic
  interpreter or dynamic section. The same test is included in the Linux CI job.

An earlier version of the demo, before the byte-order, percent-encoding, and
checksum checks were added, measured 10,752 bytes at `-O0` and 6,144 bytes at
`-O2` on Windows. These are illustrative measurements, not size guarantees.

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
