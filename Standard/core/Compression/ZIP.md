# Reading ZIP archives

`Standard::Compression::ZipArchive` opens a ZIP image in memory and indexes its
entries. Entries can be stored or DEFLATE-compressed.

```csharp
using Standard::Collections;
using Standard::Compression;
using Standard::IO;
using Standard::Text;

int main()
{
    const nuint Mebibyte = 1048576;
    Vector<byte> image = File::ReadAllBytes("assets.zip", 16 * Mebibyte);
    ZipArchive archive = new(image.AsReadOnly());
    for (nuint index = 0; index < archive.Count; index++)
    {
        ZipArchiveEntry entry = archive.Entries[index];
        String name = entry.FullName;
        Console::WriteLine(name.Data, name.Length);
        if (!entry.IsDirectory)
        {
            Vector<byte> contents = entry.ReadAllBytes(Mebibyte);
            // Use the counted bytes in contents here.
        }
    }
    return 0;
}
```

## Archive and entry lifetimes

Keep the image alive and unchanged while using the archive or any copied entry.
The archive owns its entry index. Copying an archive creates another index that
borrows the same image. Copying an entry preserves its image references, so the
entry can outlive the index it came from.

`Entries` returns a borrowed readonly span over the index. Destroying or replacing
the archive invalidates that span. Entries remain in central-directory order.

The constructors accept `Span<readonly(byte)>` or a readonly byte pointer and
count. Invalid or unsupported metadata throws `IOException` during construction.
Payload decompression and CRC validation happen when reading an entry.

## Reading an entry

| Member | Result |
| --- | --- |
| `Length` | Uncompressed byte count as `ulong` |
| `CompressedLength` | Stored payload byte count as `ulong` |
| `Method` | ZIP method number: 0 for stored, 8 for DEFLATE |
| `CRC32` | Expected checksum of the uncompressed bytes |
| `Read(output, capacity)` | Reads into caller storage and returns the byte count |
| `ReadAllBytes(maximumBytes)` | Returns an owning `Vector<byte>` within the supplied size bound |

`Read` buffer arguments:

- `output`: writable storage separate from the archive image
- `capacity`: available bytes. A value below `Length` throws `IOException` before writing

Corrupt data throws `IOException` and can leave partial or complete output in the
caller's buffer. Reads verify compressed consumption, uncompressed length, and CRC32.

`ReadAllBytes(maximumBytes)` rejects entries above the size bound before allocating.

## Names

- `NameBytes` borrows the counted filename bytes from the central directory
- `IsUtf8Name` reports ZIP's UTF-8 name flag, bit 11
- `FullName` returns an owning UTF-8 `String`. Flagged names must be valid UTF-8.
  Other names are decoded as CP437
- `IsDirectory` tests whether the raw name ends in `/`

The Unicode Path extra field, 0x7075, is ignored. Decoding preserves separators
and `.`/`..` path components from the archive name.

## Accepted archives

The reader accepts single-disk ZIP32 archives, archive comments, local extra
fields, and data descriptors with or without their optional signature. Empty
entries are accepted, including method-8 entries with zero compressed bytes.

ZIP64, encryption, patched data, unsupported flags, and compression methods other
than stored or DEFLATE throw `IOException`. The required extraction version must
be between 1.0 and 2.0, with 2.0 required for DEFLATE. Headers and payload ranges must fit
inside the image, and local and central metadata must agree. Unknown extra fields
are skipped after checking their lengths.

Metadata scanning has a budget of four times the image length plus 65,536 bytes,
saturating at the addressable size limit. Exceeding it throws `IOException`.

Format reference: [PKWARE ZIP APPNOTE](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT).
