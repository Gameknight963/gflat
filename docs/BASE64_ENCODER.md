# Streaming Base64 output

`Standard::Encoding::Base64Encoder` accepts bytes through `ITextWriter` and sends
padded [Base64](BINARY_CODECS.md) to another writer.

## Copy a file through an encoder

Use distinct input and output files. `OpenWrite` truncates the destination.

```csharp
using Standard;
using Standard::IO;
using Standard::Encoding;

int main()
{
    File input = File::OpenRead("input.bin");
    File output = File::OpenWrite("output.txt");
    Base64Encoder encoder = new(&output);
    char[4096] buffer;
    input.CopyTo(&encoder, buffer, 4096);
    encoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Close the output explicitly to report buffered file errors.

## Chunk boundaries and completion

`Write(data, count)` accepts chunks of any size. Complete three-byte groups
produce four characters. Up to two remaining bytes wait for another write.

Call `Complete()` to emit the final padded group. Writing `a`, then `bc`, yields
`YWJj`. Writing only `a` produces output when completion emits `YQ==`. Empty
input remains empty. Repeated completion succeeds, but writing afterward throws
`Exception`.

Scope exit discards pending bytes. It does not finish the encoded stream.

## Borrowing, copies, and failures

Keep the destination alive and at the same address. Input must be separate from
storage the destination may change. Encoding a String's bytes into that same
String can invalidate the input.

A destination error can leave partial output and permanently fails the encoder.
Further `Write` and `Complete` calls throw. Handle the partial output before
starting another encoder.

Copies share the destination but duplicate pending bytes and completion/failure
state. Use one encoder per logical stream. Interleaving copies can duplicate
prefixes, and a failure in one copy leaves older copies unaware of it.

Source: `core/Encoding/Base64Encoder.gf`
