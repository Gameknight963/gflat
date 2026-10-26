namespace std::Encoding;

// Owning String conveniences, separate from the allocation-free buffer codec.
public static class HexText
{
    public static String Encode(immutable(char*) data, nuint count, bool uppercase = true)
    {
        nuint length = 0;
        if (!Hex::TryGetEncodedLength(count, ref length)) throw new Exception("Hex length overflow");
        String result = new String();
        result.Reserve(length);
        char[2] chunk;
        for (nuint index = 0; index < count; index++)
        {
            nuint written = 0;
            Hex::TryEncode(data + index, 1, chunk, 2, ref written, uppercase);
            result.AppendLiteral(chunk, written);
        }
        return move result;
    }

    public static String Decode(immutable(char*) text, nuint count)
    {
        nuint length = 0;
        if (!Hex::TryGetDecodedLength(text, count, ref length)) throw new Exception("Invalid hexadecimal text");
        String result = new String();
        result.Reserve(length);
        char[1] chunk;
        for (nuint index = 0; index < count; index += 2)
        {
            nuint written = 0;
            Hex::TryDecode(text + index, 2, chunk, 1, ref written);
            result.AppendLiteral(chunk, written);
        }
        return move result;
    }
}
