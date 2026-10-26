# Caller-owned text output

`std/core/Text/BufferWriter.gf` defines `std::Text::BufferWriter : ITextWriter`.
Include `core/Text/BufferTooSmallException.gf` too. Formatting contracts are
automatically available; primitive formatting implementations live in
`core/Formatting.gf`. Neither the writer nor integer formatting requires
`String` or libc.

Construct a writer over writable storage:

```csharp
char[128] bytes;
std::Text::BufferWriter writer = new std::Text::BufferWriter(bytes, 128);
writer.Write("value: ");
int value = 42;
value.Format(&writer);
```

The constructor's size includes space for a trailing null. `Capacity` reports
the usable payload size (`127` here), matching `String.Capacity`; `Length`
counts appended bytes, and `Remaining` is `Capacity - Length`. `Data` is a
borrowed read-only pointer. The caller owns the storage and must keep it valid
for the writer's lifetime and for any subsequent use of that pointer.

`Write(data, count)` preserves embedded nulls, appends all bytes, and restores
the final null terminator. `Write(text)` scans a null-terminated C string.
Each write checks capacity first: an overflow throws `BufferTooSmallException`
without modifying the buffer or the writer's length. Formatting can make
multiple writes; if a later write fails, earlier successful writes remain.

`TryWrite(data, count)` and `TryWrite(text)` provide the same counted and
C-string operations without throwing on capacity exhaustion. Both are
`nothrow`: false leaves every buffer byte and `Length` unchanged, while true
appends all bytes and maintains the terminator. `Write` delegates to these
methods and throws only if they return false. There is one shared copy path.

Source buffers may overlap the destination. Copying follows `memmove` order,
including appending the writer's own data. `Clear()` resets the length and
terminator without freeing or allocating anything.
Overlap may include the current terminator as counted payload, or initialized
bytes retained beyond `Length` after `Clear()`. Sources must still be readable
for their stated count; C-string input must have a readable terminator and is
measured before any destination bytes change. Neither form reads uninitialized
tail storage on the caller's behalf.

A constructor size of zero is supported: construction, `Clear`, and empty
writes do not touch the supplied pointer. `Capacity` and `Remaining` are zero,
nonempty writes throw, and `Data` is not guaranteed to point to a terminated
string. A one-byte buffer can hold only the terminator.
The `TryWrite` counterparts return false for nonempty input. A zero-count
`TryWrite` returns true without reading either buffer, including a zero-sized
writer; its C-string overload still reads input to find the terminator.

Copying a writer borrows the same storage with an independent position; writes
through either copy affect that shared storage. The writer does not synchronize
access or track changes made through the caller's original pointer.

The private console/file line readers deliberately use different writers that
keep a truncated prefix on overflow. This public writer does not truncate.
