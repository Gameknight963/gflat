# FileStream byte buffers

`FileStream` accepts byte pointers directly in `Read`, `ReadExactly`, and `Write`.
`WriteByte(byte)` writes a single unsigned byte. Both scratch-buffer `CopyTo`
forms also accept `byte*`, whether the destination is a file or an `ITextWriter`.

```csharp
using Standard::IO;

int main()
{
    FileStream file = FileStream::OpenTemporary();
    byte[3] data = [(byte)0, (byte)171, (byte)255];
    file.Write(&data[0], 3);
    file.Position = 0l;
    byte[3] output;
    file.ReadExactly(&output[0], 3);
    if (output[1] != (byte)171 || output[2] != (byte)255) return 1;
    file.Close();
    return 0;
}
```

Counts and capacities are bytes. These overloads retain the character-buffer
methods' behavior: reads can be short at EOF, exact reads throw on early EOF,
and closed streams reject even zero-length calls. Errors may leave consumed
input, written buffer prefixes, or partial file output.

Copy scratch must be nonempty and independent of source and destination.
Copying into the same FileStream object is rejected before reading. Both
endpoints stay open after copying. Byte overloads do not allocate conversion
buffers. Normal libc buffering and destination allocations still apply.
