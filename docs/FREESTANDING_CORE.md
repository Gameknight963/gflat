# Core utilities without a runtime

`Ascii`, `Utf8`, `Utf16`, `UnicodeTranscoding`, `Base64`, `Hex`, `PercentEncoding`, `BinaryPrimitives`, `Hashing`,
`BitOperations`, `CRC32`, `Adler32`, `Utf8Decoder`, `BinaryBufferReader`,
`BinaryBufferWriter`, `IntegerFormatting`, the Try-only `Text/hex_integer_parsing.gf`,
`Text/boolean_parsing.gf`, `ByteSearch`, `CString`, `JsonString`, `VarInt`, and the raw
byte operations in `Memory/bytes.gf` can operate on caller-owned buffers
without libc, allocation, or the gflat exception runtime. Their buffer
and scalar functions are `nothrow`. Malformed text and insufficient capacity are
reported through their return values. Caller pointers must still designate valid
memory for the specified counts.

The compiler emits ordinary calls to known `nothrow` functions, including
calls through virtual/interface contracts carrying that guarantee. Function
pointers do not encode that promise and remain conservative. A `nothrow` function
terminates if it encounters an unhandled exception.

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

The test runs at `-O0` and `-O2`. A separate assembly object supplies input values
so LLVM must execute the operations rather than replace them with constant results.

Coverage includes:

- **Unicode:** UTF-8 and UTF-16 scalar round trips, embedded NUL, malformed suffixes,
  and unchanged output on validation or capacity failure
- **Binary data:** mixed byte order, unaligned scalars, bounded child cursors,
  checksums, canonical varints, and exact floating-point bit patterns
- **Text:** decimal and hexadecimal limits, Boolean parsing, counted searches,
  bounded C-string operations, and JSON escaping
- **Memory:** guarded copy and fill operations, overlapping moves in both directions,
  and zero-count operations

The final executable is inspected too:

- **Windows x64:** imports exactly `KERNEL32.dll!ExitProcess` and has no CLR header
- **Linux x64:** uses a syscall exit stub and has no dynamic interpreter or dynamic section

The test source contains the individual cases and expected results.

## Windows floating-point marker

On Windows x64, LLVM also emits the `_fltused` ABI marker for floating-point
definitions. Even an integer-only caller that includes `BinaryPrimitives.gf`
can need this marker at O0 because unused floating-point definitions remain in
the object. A focused test compiles that minimal integer-only client at O0 and
inspects its COFF symbol table for the undefined marker.

The test startup supplies the data definition `@_fltused = global i32 0`.
[LLVM's maintainers describe this startup responsibility](https://discourse.llvm.org/t/lld-link-fails-to-link-uefi-binary-because-of-undefined-symbol-fltused/52454).
A freestanding program must also supply its platform startup, floating-point
environment initialization where needed, and any other required ABI definitions.

## Limits

`NoStdLib` selects sources. The test supplies startup and disables default native
linker libraries separately. `CompilationOptions.NoBuiltins` disables LLVM's recognition and
introduction of known libc/libm builtins in compiler-generated functions. It is
false by default, independent of `NoStdLib`. Generated methods, helpers, lambdas,
interface thunks, and inline LLVM body wrappers receive the `"no-builtins"`
function attribute. Arbitrary `__llvm_module` definitions retain the attributes
their author supplied. Ordinary optimization passes still run.

This optimizer policy does not prohibit explicit libc calls, nor does it prevent
existing memory intrinsics from lowering to external `memcpy` or `memset` calls.
Platform ABI helpers, stack probes, and arithmetic support are also separate.
Final executable inspection remains the evidence for the particular tested
program's runtime independence. LLVM's
[TargetLibraryInfo implementation](https://llvm.org/doxygen/TargetLibraryInfo_8h_source.html)
defines the attribute's library-availability behavior.
Other core facilities have additional requirements. Owning containers need
`Memory::Allocate`/`Free`. Throwing operations need exception support. `PseudoRandom`
currently includes throwing bounded methods in the same type, so selecting that
source also requires the relevant exception and allocation definitions. Libc file,
console, and floating-text APIs retain their documented host dependencies.
