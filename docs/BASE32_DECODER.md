# Streaming Base32 decoding

`Standard::Encoding::Base32Decoder` accepts encoded chunks through `ITextWriter`
and sends decoded bytes to another writer.

## Decode counted input through a writer pipeline

Use distinct input and output files. `OpenWrite` truncates the destination.

```csharp
using Standard::IO;
using Standard::Encoding;

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    File input = File::OpenRead(argv[1]);
    File output = File::OpenWrite(argv[2]);
    Base32Decoder decoder = new(&output);
    IByteReader* source = &input;
    source.CopyTo(&decoder);
    decoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Close the output explicitly to report buffered file errors. For a framed network
message, bound the encoded payload instead of copying until connection EOF.

## Strict profile and completion

The decoder accepts the uppercase padded [Base32](BASE32.md) profile. Each
complete eight-character group is validated before decoding. Groups can span
several `Write` calls. A padded group ends encoded input, so further nonempty
input throws.

Call `Complete()` to check that the final group is complete. `MY======` decodes
to `f`, while `MY` fails at completion. Empty input is valid. Repeated successful
completion is allowed, but any subsequent `Write` throws `Exception`.

Scope exit discards pending text. Explicit completion is needed to detect a
truncated final group.

## Prefix progress and failures

Invalid input, incomplete completion, or a destination exception permanently
fails the decoder. Later `Write` and `Complete` calls throw.

A failure can leave output from earlier groups, while a decoded batch waiting
inside the failing call may be discarded. Handle partial output before starting
another decoder. For validation before any output, use whole-buffer
`Base32::TryDecode`.

## Borrowing and copies

Keep the destination alive and at the same address. Input must be separate from
storage the destination may modify. Decoding a String into that same String can
invalidate input.

Copies share the destination but duplicate pending text and status. Use one
decoder per logical stream. Interleaving copies can duplicate output, and a
failure in one copy leaves older copies unaware of it.

## Explicit sources

Sources: `core/Encoding/Base32Decoder.gf`, `core/Encoding/Base32.gf`, and the
text writer and exception contracts.
