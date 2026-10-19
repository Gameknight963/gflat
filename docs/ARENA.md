# Arena storage

`std::Arena` groups temporary raw allocations into backing blocks obtained from
`Memory::Allocate`. Each arena has independent state; it does not replace the
program's default allocator. Growing an arena leaves earlier addresses valid.

```csharp
using std;

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
- Invalid alignment and overflowing size calculations throw `Exception`.
  Backing allocation failure throws `BadAllocException`. Existing blocks and
  their free space remain usable after failure.
- `Reset()` frees all backing blocks and leaves the arena reusable. Destruction
  performs the same cleanup. Copying an arena is prohibited; moving transfers
  ownership of its blocks.

## Object lifetimes

An arena owns **storage**, not the objects placed there. `Reset` and destruction
do not run object destructors. Construct objects explicitly with placement
construction, and run their destructors before releasing storage when required.
This includes compiler-generated cleanup for objects containing managed
references, even when the type has no user-written destructor.

```csharp
String* text = new* String()
    at (String*)scratch.Allocate((nuint)sizeof(String), (nuint)alignof(String));
text.Append("temporary");
text.~String(); // Release String's separately owned character buffer.
scratch.Reset();
```

Never `delete` an arena pointer or pass it to `Memory::Free`: it points inside a
backing block. Every returned pointer becomes invalid on reset or destruction.
Pointers remain valid when the arena value moves, provided the new owner remains
alive. Arenas are not synchronized; use independent instances or external
synchronization across threads.
