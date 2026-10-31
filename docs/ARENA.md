# Arena storage

`Standard::Memory::Arena` groups temporary allocations into backing blocks.
Growing the arena preserves earlier addresses. Reset or destruction frees every
block together.

```csharp
using Standard;
using Standard::Memory;

int main()
{
    Arena scratch = new(4096);
    int* number = (int*)scratch.Allocate((nuint)sizeof(int), (nuint)alignof(int));
    *number = 42;
    return *number - 42;
}
```

| Operation | Effect |
| --- | --- |
| `new Arena(blockSize = 4096)` | Sets a preferred positive block size |
| `Allocate(bytes, alignment)` | Reserves aligned raw storage |
| `AllocateZeroed(bytes, alignment)` | Reserves and clears raw storage |
| `Reset()` | Frees all blocks and leaves the arena reusable |

Alignment defaults to `alignof(nuint)` and must be a positive power of two.
Requests larger than the preferred size get larger blocks. Zero-byte requests
reserve one byte with a distinct address until reset.

Invalid alignment or size overflow throws `Exception`. Allocation failure throws
`BadAllocException` and preserves existing allocations. Arena is move-only.
Moving preserves allocated addresses under the new owner's lifetime.

## Object lifetimes

Use zeroed storage for raw byte data:

```csharp
byte* header = (byte*)scratch.AllocateZeroed(24, 1);
header[0] = (byte)1; // Protocol version; the remaining header bytes start at zero.
```

For objects, use placement construction and run their destructors before reset:

```csharp
using Standard::Text;

{
    String* text = new* String()
        at (String*)scratch.Allocate((nuint)sizeof(String), (nuint)alignof(String));
    defer { text.~String(); } // Release the owned buffer on normal or exceptional exit.
    text.Append("temporary");
}
scratch.Reset();
```

Register cleanup after construction succeeds. This also applies to generated
cleanup for managed fields. Arena pointers refer into backing blocks, so release
them through the arena rather than `delete` or `Memory::Free`.

Source: `core/Memory/Arena.gf`
