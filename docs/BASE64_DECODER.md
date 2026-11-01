# Streaming Base64 decoding

`Standard::Encoding::Base64Decoder` accepts encoded chunks through `ITextWriter`
and sends decoded bytes to another writer.

## Decode a file into binary output

Use distinct input and output files. `OpenWrite` truncates the destination.

```csharp
using Standard;
using Standard::IO;
using Standard::Encoding;

int main()
{
    FileStream input = File::OpenRead("input.txt");
    FileStream output = File::OpenWrite("output.bin");
    Base64Decoder decoder = new(&output);
    char[4096] buffer;
    input.CopyTo(&decoder, buffer, 4096);
    decoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Close the output explicitly to report buffered file errors.

## Strict input and completion

Input follows the padded [Base64](BINARY_CODECS.md) profile. Four-character
groups can span several `Write` calls. A padded group ends encoded input, so
further nonempty input throws.

Call `Complete()` to check the final group. Writing `Y`, then `WJ`, then `j`
emits `abc`. `YQ==` emits `a`. `YQ` fails at completion. Empty input is valid.
Repeated successful completion is allowed, but any subsequent `Write` throws
`Exception`.

Scope exit discards pending text. Explicit completion is needed to detect a
truncated final group.

## Failure and borrowing

Invalid input, incomplete completion, or a destination exception permanently
fails the decoder. Later `Write` and `Complete` calls throw.

A failure can leave output from earlier groups, while a decoded batch waiting
inside the failing call may be discarded. Handle partial output before starting
another decoder. For validation before any output, use whole-buffer
`Base64::TryDecode`.

Keep the destination alive and at the same address. Input must be separate from
storage the destination may modify. Decoding a String into that same String can
invalidate input.

Copies share the destination but duplicate pending text and status. Use one
decoder per logical stream. Interleaving copies can duplicate output, and a
failure in one copy leaves older copies unaware of it.

Source: `core/Encoding/Base64Decoder.gf`
