# Arena storage

`std::Memory::Arena` groups temporary raw allocations into backing blocks obtained from
`Memory::Allocate`. Each arena has independent state; it does not replace the
program's default allocator. Growing an arena leaves earlier addresses valid.

```csharp
using std; using std::Memory;

int main()
{
    Arena scratch = new(4096);
    int* number = (int*)scratch.Allocate((nuint)sizeof(int), (nuint)alignof(int));
    *number = 42;
    return *number - 42;
}
```

- `new Arena()` uses a preferred backing block size of 4096 bytes. A different
  positive size can be supplied to the constructor; larger requests receive
  larger blocks.
- `Allocate(bytes, alignment)` returns non-null raw storage. Alignment defaults
  to `alignof(nuint)` and must be a positive power of two. Larger alignments,
  including 256-byte alignment, are supported by padding within a backing block.
- Zero-byte requests reserve one byte, so successful requests have distinct
  addresses before reset.
- `AllocateZeroed(bytes, alignment)` has the same allocation and alignment rules
  and clears the reserved bytes. It makes no additional backing allocation.
  A zero-byte request still reserves one byte, which may also be cleared; callers
  cannot infer any additional usable space beyond the requested reservation.
- Invalid alignment and overflowing size calculations throw `Exception`.
  Backing allocation failure throws `BadAllocException`. Existing blocks and
  their free space remain usable after failure.
- `Reset()` frees all backing blocks and leaves the arena reusable. Destruction
  performs the same cleanup. Copying an arena is prohibited; moving transfers
  ownership of its blocks.

`Reset()` is `nothrow`: it only walks raw block headers and calls the
nonthrowing `Memory::Free` contract. It allocates no storage and invokes no
placement-object destructors. As with `Memory::Free`, `nothrow` means no
exception escapes; it does not promise recovery if a deallocator violates its
contract and terminates the process.

## Object lifetimes

For example, a scratch packet header can start with all reserved flag bytes clear:

```csharp
byte* header = (byte*)scratch.AllocateZeroed(24, 1);
header[0] = (byte)1; // Protocol version; the remaining header bytes start at zero.
```

Zeroing raw bytes does not call a constructor or create an initialized object of
an arbitrary type. Use placement construction for those objects.

An arena owns **storage**, not the objects placed there. `Reset` and destruction
do not run object destructors. Construct objects explicitly with placement
construction, and run their destructors before releasing storage when required.
This includes compiler-generated cleanup for objects containing managed
references, even when the type has no user-written destructor.

```csharp
using std::Text;
{
    String* text = new* String()
        at (String*)scratch.Allocate((nuint)sizeof(String), (nuint)alignof(String));
    defer { text.~String(); } // Release the owned buffer on normal or exceptional exit.
    text.Append("temporary");
}
scratch.Reset();
```

The inner scope finishes the object's lifetime before `Reset` releases its
storage. Register cleanup after construction succeeds, and keep the arena alive
until that cleanup runs.

Never `delete` an arena pointer or pass it to `Memory::Free`: it points inside a
backing block. Every returned pointer becomes invalid on reset or destruction.
Pointers remain valid when the arena value moves, provided the new owner remains
alive. Arenas are not synchronized; use independent instances or external
synchronization across threads.
