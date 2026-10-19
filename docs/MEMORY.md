# Raw byte operations

`Memory::Copy`, `Move`, `Fill`, `Compare`, and `Equal` operate on raw storage.
Their source file, `std/core/Memory/Bytes.gf`, is independent of the default
allocator implementation in `std/libc/Memory/Memory.gf`.

| Operation | Signature and meaning |
| --- | --- |
| `Copy(destination, source, count)` | Copy `count` bytes between **nonoverlapping** regions. |
| `Move(destination, source, count)` | Copy `count` bytes with overlap permitted, including identical regions. |
| `Fill(destination, value, count)` | Set `count` bytes to a `byte` value. |
| `Compare(left, right, count)` | Lexicographic unsigned-byte comparison; returns the first differing byte from left minus right, or zero. |
| `Equal(left, right, count)` | Compare exactly `count` bytes for equality. |

Destinations are non-null `void*`; inputs are non-null `readonly(void)*`; counts
are `nuint`. Every operation is `nothrow`. They allocate no storage, run no
destructors, and perform no accesses for zero counts. Zero counts do not relax
the language's non-null argument requirements.

```csharp
byte[32] storage;
byte* bytes = storage;
Memory::Fill((void*)bytes, (byte)0, 32);
bytes[0] = (byte)42;
Memory::Move((void*)(bytes + 1), (readonly(void)*)bytes, 16);
```

The caller must provide live readable/writable storage covering each region.
These are not generic object-copying or ownership operations. In particular,
copying resource-owning values can duplicate ownership, moving object bytes can
break internal pointers, and copying managed-reference slots bypasses compiler
notifications. Use the language's normal typed copying and movement for objects.

## Native linking

The source contains ordinary byte loops and no direct libc calls. **This does
not guarantee a libc-free native binary.** LLVM can recognize these loops or
their inlined callers and replace them with `memcpy`, `memmove`, or `memset`.
Those may lower to external runtime symbols.

For example, with Clang/LLVM22 on Windows x64, compiling `Fill` at O2 changes its
loop to `llvm.memset`, and its native object has an undefined `memset` symbol.
Passing `-ffreestanding -fno-builtin` while compiling the emitted LLVM IR did not
prevent that transformation. At the same setting, the inspected standalone
`Copy`, `Move`, `Compare`, and `Equal` definitions needed no external symbols;
this is an observation of that build, not a portability guarantee.

Hosted applications can use the functions normally. For a freestanding build,
`CompilationOptions.NoBuiltins = true` prevents LLVM from recognizing these byte
loops as known libc operations. This is an explicit optimizer policy, independent
of `NoStdLib` and disabled by default. It does not disable general optimization.

The [freestanding executable test](FREESTANDING_CORE.md) selects
`Memory/Bytes.gf` without the allocator source and enables `NoBuiltins`. It
exercises all five operations at O0 and O2 on runtime-populated bytes, including
overlapping moves in both directions, identical regions, guard bytes,
unsigned comparison, and zero counts. Both Windows x64 builds link with
`-nostdlib` and `/NODEFAULTLIB`, and import exactly `KERNEL32.dll!ExitProcess`.
The same test has a Linux x64 syscall-startup branch for CI.

This verifies those particular programs, not all possible uses or targets.
Explicit memory intrinsics can still lower to runtime symbols even with
`NoBuiltins`; stack probes, arithmetic helpers, and platform ABI support are
separate requirements. A freestanding target must provide any such symbols
selected by its backend. Inspect the final executable when validating a new
configuration.
