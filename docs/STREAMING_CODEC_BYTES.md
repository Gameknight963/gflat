# Streaming codec byte input

`Base32Encoder`, `Base64Encoder`, `Base32Decoder`, `Base64Decoder`, and
`HexDecoder` accept `Write(readonly(byte)* data, nuint count)`. The borrowed-writer
`WriteHex` helper also accepts byte input. Counts are bytes, and zero bytes within
the input are data rather than terminators.

```csharp
using Standard::Encoding;
using Standard::Text;

int main()
{
    byte[3] data = [(byte)0, (byte)171, (byte)255];
    String encoded = new();
    Base64Encoder writer = new(&encoded);
    writer.Write(&data[0], 1);
    writer.Write(&data[1], 2);
    writer.Complete();
    if (!encoded.Equals("AKv/")) return 1;
    return 0;
}
```

Input is borrowed only for each call. Keep it separate from the codec state and
sink storage. The byte overloads use the existing character-buffer state
machines, including buffered partial groups, completion, invalid-input errors,
and unusable state after sink failure. Complete streaming encoders and decoders
explicitly. Even a zero-byte write still checks their state.

`WriteHex` emits each call immediately and needs no completion. The stream sink
remains an `ITextWriter` and receives counted character buffers as before.
