# Raw byte operations

`Memory::Copy`, `Move`, `Fill`, `Compare`, and `Equal` operate on raw storage.
Their source file, `Standard/core/Memory/bytes.gf`, is independent of the default
allocator implementation in `Standard/libc/Memory/allocation.gf`.

| Operation | Signature and meaning |
| --- | --- |
| `Copy(destination, source, count)` | Copy `count` bytes between **nonoverlapping** regions. |
| `Move(destination, source, count)` | Copy `count` bytes with overlap permitted, including identical regions. |
| `Fill(destination, value, count)` | Set `count` bytes to a `byte` value. |
| `Compare(left, right, count)` | Lexicographic unsigned-byte comparison. Returns the first differing byte from left minus right, or zero. |
| `Equal(left, right, count)` | Compare exactly `count` bytes for equality. |

Destinations are non-null `void*`. Inputs are non-null `readonly(void)*`. Counts
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

## Allocator cleanup contract

The allocator source provides `Memory::Allocate(nuint)` and
`Memory::Free(void*) nothrow`. Replace both when supplying a custom allocator.
The replacement `Free` must also declare `nothrow`. Allocator definitions supplied
without Standard have the same requirement.

`Allocate` can fail or throw before a container moves its existing values.
`Free` must release valid storage without propagating an exception. Containers
also use it after relocating values, when the old allocation no longer contains
initialized objects. An escaping exception would leave that operation incomplete.

This uses the ordinary `nothrow` contract: errors may be caught inside `Free`,
but an exception escaping it reaches the unhandled-exception boundary instead
of propagating to its caller. It does not prove that the allocator is correct or
make invalid pointers safe to free. Freeing storage does not run object destructors.

The default allocator keeps its private C imports in an implementation namespace.
Application files may declare their own global `malloc` and `free` imports,
including private declarations, without changing the default wrappers' lookup.
Replacement hooks may continue calling their own imports normally. All declarations
for the same native C symbol still need compatible native signatures and matching
`nothrow` contracts. The allocator does not override that general rule.
## Native linking

The source contains ordinary byte loops and no direct libc calls. **This does
not guarantee a libc-free native binary.** LLVM can recognize these loops or
their inlined callers and replace them with `memcpy`, `memmove`, or `memset`.
Those may lower to external runtime symbols.

For example, with Clang/LLVM22 on Windows x64, compiling `Fill` at O2 changes its
loop to `llvm.memset`, and its native object has an undefined `memset` symbol.
Passing `-ffreestanding -fno-builtin` while compiling the emitted LLVM IR did not
prevent that transformation. At the same setting, the inspected standalone
`Copy`, `Move`, `Compare`, and `Equal` definitions needed no external symbols.
This is an observation of that build, not a portability guarantee.

Hosted applications can use the functions normally. For a freestanding build,
`CompilationOptions.NoBuiltins = true` prevents LLVM from recognizing these byte
loops as known libc operations. This is an explicit optimizer policy, independent
of `NoStdLib` and disabled by default. It does not disable general optimization.

The [freestanding executable test](FREESTANDING_CORE.md) selects
`Memory/bytes.gf` without the allocator source and enables `NoBuiltins`. It
exercises all five operations at O0 and O2 on runtime-populated bytes, including
overlapping moves in both directions, identical regions, guard bytes,
unsigned comparison, and zero counts. Both Windows x64 builds link with
`-nostdlib` and `/NODEFAULTLIB`, and import exactly `KERNEL32.dll!ExitProcess`.
The same test has a Linux x64 syscall-startup branch for CI.

This verifies those particular programs, not all possible uses or targets.
Explicit memory intrinsics can still lower to runtime symbols even with
`NoBuiltins`. Stack probes, arithmetic helpers, and platform ABI support are
separate requirements. A freestanding target must provide any such symbols
selected by its backend. Inspect the final executable when validating a new
configuration.

## Integer alignment

`Memory::AlignUp(value, alignment)` rounds an integer byte offset/address upward.
`AlignDown` rounds downward. Alignment must be a positive power of two, matching
Arena's alignment requirement. Invalid alignment throws `Exception`. AlignUp
also throws if the rounded result cannot fit in `nuint`. AlignDown cannot
overflow. An already aligned input is returned unchanged.

`Memory::IsAligned(value, alignment)` is nothrow and returns false for invalid
alignment, including zero. For valid alignment, offset zero is aligned.

```cpp
int main()
{
    nuint headerBytes = 13;
    nuint payloadOffset = Memory::AlignUp(headerBytes, 8); // 16
    nuint pageStart = Memory::AlignDown(123, 64); // 64
    if (payloadOffset != 16 || pageStart != 64) return 1;
    if (!Memory::IsAligned(payloadOffset, 8)) return 2;
    return 0;
}
```

These functions operate only on integers. They allocate no storage, do not
validate backing storage or buffer size, and do not construct objects. A caller
laying out a real buffer must independently check its capacity and any additional
size arithmetic.
Selective source users can include `Standard/core/Memory/alignment.gf` separately.
Its throwing helpers require ordinary exception support. Success performs no
allocation. Exception runtime behavior still applies to failures.
