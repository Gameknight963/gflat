# Core utilities without a runtime

Buffer-oriented core APIs can run in a freestanding program with application
startup code and explicitly selected sources.

| Area | Examples |
| --- | --- |
| Bytes and text | Ascii, CString, ByteSearch, IntegerFormatting, Try parsers |
| Encoding | Utf8, Utf16, UnicodeTranscoding, Base64, Hex, PercentEncoding |
| Binary data | BinaryPrimitives, BinaryBufferReader/Writer, VarInt, checksums |
| Raw storage | Memory byte operations and bit operations |

These APIs report malformed input or insufficient capacity through return values.
Owning containers additionally need `Memory::Allocate` and `Free`. Throwing APIs
need the exception runtime, including when unused throwing definitions share a
selected file with Try methods.

## Verified executable

[`FreestandingCoreUtilitiesTests`](../gflat.Tests/FreestandingCoreUtilitiesTests.cs)
builds a G♭ `main` with selected sources, `NoStdLib`, `NoBuiltins`, and internal
function linkage. Its startup calls main and exits through the operating system.

```console
dotnet test gflat.Tests/gflat.Tests.csproj --filter FullyQualifiedName~FreestandingCoreUtilitiesTests
```

Tests run at O0 and O2 with input supplied by a separate assembly object. They
check Unicode, binary encodings, parsing, checksums, bounded buffers, and memory
operations. Final executable checks are:

- **Windows x64:** only `KERNEL32.dll!ExitProcess` is imported, with no CLR header
- **Linux x64:** syscall exit, with no dynamic interpreter or dynamic section

## Windows floating-point marker

LLVM emits an `_fltused` marker for Windows floating-point definitions. Including
BinaryPrimitives can require it at O0 even for an integer-only caller. The test
startup supplies `@_fltused = global i32 0`. See
[LLVM's startup discussion](https://discourse.llvm.org/t/lld-link-fails-to-link-uefi-binary-because-of-undefined-symbol-fltused/52454).

## Limits

`NoStdLib` controls source selection. Supply platform startup and disable default
linker libraries separately. `NoBuiltins` controls LLVM recognition of libc/libm
operations while leaving ordinary optimization enabled.

Explicit memory intrinsics, stack probes, arithmetic support, and other ABI
helpers may still require definitions. Inspect the final executable for each
startup and target configuration.
