# Atomic integer operations

`Standard::Threading::Interlocked` provides sequentially consistent operations
on `int` and `long`. `T` below denotes either integer type.

| Operation | Result |
| --- | --- |
| `Read(ref readonly(T) location)` | Current value |
| `Exchange(ref T location, T value)` | Previous value |
| `CompareExchange(ref T location, T value, T comparand)` | Observed value, replacing it when equal to `comparand` |
| `Add(ref T location, T value)` | Updated sum |
| `Increment(ref T location)` | Updated value after adding one |
| `Decrement(ref T location)` | Updated value after subtracting one |

CompareExchange is strong: an observed value equal to `comparand` means replacement
succeeded. Arithmetic wraps at the integer width.

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

For selective compilation:

```powershell
dotnet gflat/bin/Debug/net10.0/gflat.dll main.gf Standard/core/Threading/Interlocked.gf --no-stdlib --no-builtins --run
```

## Shared storage

Locations require four-byte alignment for `int` or eight-byte alignment for `long`.
Keep them alive until every thread has finished. All concurrent accesses to the
same location must use compatible atomic operations of the same width. Ordinary
access is suitable before workers start or after they join.

Atomic integer operations apply to those locations. Synchronization of a larger
data structure remains its own design. Backend lowering determines whether an
operation is lock-free on a particular target.

Ordering follows [LLVM's atomic rules](https://llvm.org/docs/LangRef.html#ordering)
and [strong cmpxchg semantics](https://llvm.org/docs/LangRef.html#cmpxchg-instruction).

Source: `core/Threading/Interlocked.gf`
