namespace std::Encoding;

// Stream counted bytes as hexadecimal to a borrowed writer.
public void WriteHex(ITextWriter* output, readonly(char)* data, nuint count, bool uppercase = true)
{
    nuint required = 0;
    if (!Hex::TryGetEncodedLength(count, ref required))
        throw new Exception("Hex length overflow");
    char[256] encoded;
    nuint offset = 0;
    while (offset < count)
    {
        nuint amount = count - offset;
        if (amount > 128) amount = 128;
        nuint written = 0;
        if (!Hex::TryEncode(data + offset, amount, encoded, 256, ref written, uppercase))
            throw new Exception("Hex writer block encoding failed");
        output.Write(encoded, written);
        offset += amount;
    }
}

extension ITextWriter
{
    public void WriteHex(readonly(char)* data, nuint count, bool uppercase = true)
    => std::Encoding::WriteHex(this, data, count, uppercase);
}
