# Streaming Base32 output

`Standard::Encoding::Base32Encoder` accepts bytes through `ITextWriter` and sends
uppercase padded [Base32](BASE32.md) to another writer.

## Copy counted input through an encoder

Use distinct input and output files. `OpenWrite` truncates the destination.

```csharp
using Standard::IO;
using Standard::Encoding;

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    File input = File::OpenRead(argv[1]);
    File output = File::OpenWrite(argv[2]);
    Base32Encoder encoder = new(&output);
    IByteReader* source = &input;
    source.CopyTo(&encoder);
    encoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Close the output explicitly to report buffered file errors. For a framed network
message, copy its bounded payload rather than waiting for connection EOF.

## Chunks and final padding

`Write(data, count)` accepts chunks of any size. Complete five-byte groups produce
eight characters. Up to four remaining input bytes wait for another write.

Call `Complete()` to emit the final padded group. For example, writing `f` produces
output only when completion emits `MY======`. Empty input remains empty. Repeated
completion succeeds, but writing afterward throws `Exception`.

Scope exit discards a pending tail. It does not finish the encoded stream.

## Borrowing, copies and errors

Keep the destination alive and at the same address. Input must be separate from
storage the destination may change. Encoding a String's bytes into that same
String can invalidate the input.

A destination error can leave partial output and permanently fails the encoder.
Further `Write` and `Complete` calls throw. Handle the partial output before
starting another encoder.

Copies share the destination but duplicate pending bytes and completion/failure
state. Use one encoder per logical stream. Interleaving copies can duplicate
prefixes, and a failure in one copy leaves older copies unaware of it.

## Explicit source selection

Sources: `core/Encoding/Base32Encoder.gf`, `core/Encoding/Base32.gf`, and the
text writer and exception contracts.
