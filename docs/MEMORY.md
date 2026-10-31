# Raw byte operations

The global `Memory` namespace provides byte operations:

| Operation | Effect |
| --- | --- |
| `Copy(destination, source, count)` | Copies between separate regions |
| `Move(destination, source, count)` | Copies with overlap permitted |
| `Fill(destination, value, count)` | Sets bytes to one `byte` value |
| `Compare(left, right, count)` | Returns first differing unsigned byte from left minus right, or zero |
| `Equal(left, right, count)` | Exact byte equality |

```csharp
byte[32] storage;
byte* bytes = storage;
Memory::Fill((void*)bytes, (byte)0, 32);
bytes[0] = (byte)42;
Memory::Move((void*)(bytes + 1), (readonly(void)*)bytes, 16);
```

For objects, use typed copying and movement. Raw byte operations bypass owned
resource cleanup and managed-reference notifications, and can break pointers
into the relocated object's own fields.

## Allocator cleanup contract

`Memory::Allocate(nuint)` returns storage. `Memory::Free(void*) nothrow` releases
it. Supply both replacements for a custom allocator. A replacement `Free` must
declare `nothrow` too.

Containers call Free after relocating objects out of old storage. The deallocator
must release that storage successfully. An escaping exception terminates at its
`nothrow` boundary. Object destruction is a separate operation from freeing bytes.

## Native linking

LLVM can lower byte operations to external `memcpy`, `memmove`, or `memset` calls.
Freestanding builds can enable `NoBuiltins` to suppress recognition of byte loops
as libc operations. Explicit memory intrinsics and target ABI helpers may still
need runtime definitions.

The [freestanding executable test](FREESTANDING_CORE.md) checks final Windows
and Linux binaries at O0 and O2. Use final symbol/import inspection when checking
a new target or startup configuration.

## Integer alignment

| Operation | Result |
| --- | --- |
| `AlignUp(value, alignment)` | Next aligned integer offset, or the unchanged aligned input |
| `AlignDown(value, alignment)` | Previous aligned integer offset |
| `IsAligned(value, alignment)` | Whether the offset meets the alignment |

Alignment must be a positive power of two. Invalid alignment makes `IsAligned`
return false and the rounding methods throw `Exception`. Upward overflow also
throws. Check buffer capacity separately when using an aligned offset.

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

Sources: `core/Memory/bytes.gf`, `alignment.gf`, and
`libc/Memory/allocation.gf` for the default allocator.
