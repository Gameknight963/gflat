# Streaming hexadecimal decoding

`Standard::Encoding::HexDecoder` accepts hexadecimal text through `ITextWriter`
and sends decoded bytes to another writer. Input digits are `0`–`9`, `a`–`f`,
and `A`–`F`. Whitespace, separators, and `0x` prefixes fail.

```csharp
using Standard;
using Standard::IO;
using Standard::Encoding;

int main()
{
    FileStream input = File::OpenRead("input.hex");
    FileStream output = File::OpenWrite("output.bin");
    HexDecoder decoder = new(&output);
    char[257] buffer;
    input.CopyTo(&decoder, buffer, 257);
    decoder.Complete();
    input.Close();
    output.Close();
    return 0;
}
```

Use distinct files because `OpenWrite` truncates its destination. Close output
explicitly to report buffered file errors.

## Writes and completion

`Write` accepts chunks of any size. Writing `a`, then `F`, emits byte AF. A lone
digit waits for its pair and may remain unvalidated: writing `g` alone can
succeed, but its pair or `Complete()` rejects it.

Call `Complete()` to check that the total digit count is even. Empty input is
valid. Repeated successful completion is allowed, but any subsequent `Write`
throws `Exception`. Scope exit discards a pending digit.

## Errors and copies

Invalid input, incomplete completion, or a destination exception permanently
fails the decoder. Later `Write` and `Complete` calls throw. Failure can leave
output from earlier pairs and discard a batch waiting inside the failing call.
Use whole-buffer `Hex::TryDecode` when validation must precede all output.

Keep the destination alive and at the same address. Input must be separate from
storage it may modify. Decoding a String into that same String can invalidate
input.

Copies share the destination but duplicate their pending digit and status.
For example, copies carrying `a`, followed by `0` and `f`, write A0 then AF to
the same destination. Use one decoder per logical stream.

Sources: `core/Encoding/HexDecoder.gf`, `core/Encoding/Hex.gf`
