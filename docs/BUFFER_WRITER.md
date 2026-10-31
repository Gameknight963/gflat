# Caller-owned text output

`Standard::Text::BufferWriter` implements `ITextWriter` over a character buffer:

```csharp
char[128] bytes;
Standard::Text::BufferWriter writer = new Standard::Text::BufferWriter(bytes, 128);
writer.Write("value: ");
int value = 42;
value.Format(&writer);
```

| Property or argument | Meaning in this example |
| --- | --- |
| Constructor size | 128 bytes, including room for a NUL terminator |
| `Capacity` | 127 payload bytes |
| `Length` | Appended byte count, including embedded NULs |
| `Remaining` | `Capacity - Length` |
| `Data` | Readonly pointer into the caller's buffer |

Keep the buffer alive for the writer and any retained `Data` pointers.

## Writing

- `Write(data, count)` appends counted bytes and restores the final NUL
- `Write(text)` appends bytes through the C string's first NUL
- `TryWrite` offers the same overloads and returns false on capacity exhaustion
- `Clear()` resets the length and terminator while retaining the buffer

A failed `TryWrite` preserves the buffer and length. `Write` throws
`BufferTooSmallException` instead. Formatting can perform several writes, so
earlier writes remain when a later one fails.

Input may overlap the destination, including the current terminator or initialized
spare bytes. Copying follows `memmove` order.

## Empty buffers and copies

A size-one buffer holds the terminator alone. A size-zero writer accepts empty
writes, rejects nonempty ones, and has no guaranteed terminator at `Data`.

Copies share the buffer but have independent positions. Writes through one copy
can overwrite bytes used by another. Use one writer per logical output.

Sources: `core/Text/BufferWriter.gf`, `core/Text/BufferTooSmallException.gf`
