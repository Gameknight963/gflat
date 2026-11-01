# Writing ZIP archives

`Standard::Compression::ZipArchiveWriter` writes stored entries to a borrowed
`ITextWriter`. Call `Complete()` to finish the archive.

```csharp
using Standard::Compression;
using Standard::IO;

int main()
{
    FileStream output = File::OpenWrite("notes.zip");
    ZipArchiveWriter archive = new(&output);
    byte[4] contents = [110, 111, 116, 101];
    archive.AddEntry("notes.txt", contents, 4);
    archive.Complete();
    output.Close();
    return 0;
}
```

The first byte written is the archive's origin. Start with an empty destination,
or treat the newly written segment as a separate ZIP image.

## Adding entries

`AddEntry(name, data, count)` accepts a C-string name and counted payload bytes.
The span overload accepts `Span<readonly(char)>` names and
`Span<readonly(byte)>` payloads, including names with embedded zero bytes.

- Names must contain 1 to 65,535 valid UTF-8 bytes
- Payloads may be empty. A name ending in `/` marks a directory
- Duplicate names retain insertion order
- Each entry uses stored method 0, the UTF-8 name flag, and a timestamp of
  January 1, 1980 at midnight

Entry names are copied into the writer's index. Payload bytes are consumed during
`AddEntry` and can be released afterward. Inputs must be separate from the
destination and writer-owned storage, including while the sink processes headers.

The writer permits at most 65,534 entries and a complete image smaller than
4,294,967,295 bytes. Size checks, UTF-8 validation, and metadata allocations happen
before writing an entry. A validation or allocation failure leaves the writer
usable and emits no entry bytes.

## Completing and handling failures

`Complete()` writes the central directory and end record. Repeating a successful
completion emits nothing. Adding an entry afterward throws `IOException`.

An exception from the destination can leave a partial entry or directory. The
writer then rejects both `AddEntry` and `Complete` with `IOException`. Discard that
output rather than retrying through the writer.

Keep the destination alive through completion. The writer does not flush or close
it. Destruction releases the entry index without completing the archive. Copying
the writer is prohibited. Moving transfers its index and writing state.

For reading the result, see [ZIP archives](ZIP.md).
