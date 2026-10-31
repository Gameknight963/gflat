# Atomic integer operations

`Standard::Threading::Interlocked` provides atomic operations on existing `int` and
`long` storage. Each method is `nothrow` and uses sequentially consistent ordering
across threads. Every method below has both an `int` and a `long` overload.
`T` in the table stands for that same integer type, not a generic parameter.

| API | Result and effect |
| --- | --- |
| `Read(ref readonly(T) location)` | Returns the atomically loaded value. Accepts a mutable or readonly location. |
| `Exchange(ref T location, T value)` | Replaces the value and returns its previous value. |
| `CompareExchange(ref T location, T value, T comparand)` | Writes `value` only if the observed value equals `comparand`. Returns the observed value in either case. |
| `Add(ref T location, T value)` | Adds `value` atomically and returns the updated value. |
| `Increment(ref T location)` | Adds one atomically and returns the updated value. |
| `Decrement(ref T location)` | Subtracts one atomically and returns the updated value. |

CompareExchange is strong: it does not fail spuriously when the comparison
matches. A returned value equal to `comparand` indicates that the replacement
occurred. Add, Increment, and Decrement wrap at the integer width: incrementing
`2147483647` yields `-2147483648`, and decrementing `-2147483648` yields
`2147483647`. The corresponding wraparound applies to 64-bit `long` values.

```csharp
using Standard::Threading;

int main() nothrow
{
    int count = 0;
    if (Interlocked::Increment(ref count) != 1) return 1;
    if (Interlocked::Add(ref count, 4) != 5) return 2;
    if (Interlocked::CompareExchange(ref count, 9, 5) != 5) return 3;
    if (Interlocked::CompareExchange(ref count, 11, 5) != 9) return 4;
    if (Interlocked::Exchange(ref count, 0) != 9) return 5;
    return Interlocked::Read(ref count);
}
```

With `NoStdLib`, include only `Standard/core/Threading/Interlocked.gf` alongside this
program. That file has no other source dependencies. For example, from the
repository root with the compiler already built:

```powershell
dotnet gflat/bin/Debug/net10.0/gflat.dll main.gf Standard/core/Threading/Interlocked.gf --no-stdlib --no-builtins --run
```

The program exits with zero. It illustrates operation results in one thread.
Thread creation and joining are provided separately by the application or its
native host.

Locations must designate live, initialized integer storage for every access and
must have at least 4-byte alignment for `int` or 8-byte alignment for `long`.
Forged references or unaligned packed storage do not satisfy this contract.
All concurrent overlapping accesses to a location must use compatible atomic
operations of the same integer width. Ordinary reads or writes may initialize
storage before workers start or inspect it after all workers have joined. They
must not race with atomic accesses. Keep the storage alive until every user has
finished. `Read` accepting a readonly view does not permit modifying storage that
is actually readonly through some other alias.

These integer operations do not make containers, managed-reference bookkeeping,
the collector, or unrelated application state thread safe. In particular, the
current tracing collector retains its single-thread ownership rule. The gflat implementation
contains no allocation or allocator calls, but supported targets determine how LLVM
lowers atomic instructions. Universal lock freedom and absence of target support
routines are not guaranteed. No managed-reference atomic operation or thread
creation API is supplied.

The implementation uses the existing inline LLVM facility. Sequential
consistency and strong compare-and-exchange follow the [LLVM atomic ordering
rules](https://llvm.org/docs/LangRef.html#ordering) and
[cmpxchg semantics](https://llvm.org/docs/LangRef.html#cmpxchg-instruction).
