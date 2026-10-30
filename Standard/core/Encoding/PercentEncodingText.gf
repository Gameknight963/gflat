using Standard::Text;
namespace Standard::Encoding;

// Owning String conveniences kept separate from the nothrow buffer codec.
public static class PercentEncodingText
{
    public static String Encode(immutable(char*) data, nuint count)
    {
        nuint length = 0;
        if (!PercentEncoding::TryGetEncodedLength(data, count, ref length)) throw new Exception("Percent-encoded length overflow");
        String result = new String();
        result.Reserve(length);
        char[3] chunk;
        for (nuint i = 0; i < count; i++)
        {
            nuint written = 0;
            PercentEncoding::TryEncode(data + i, 1, chunk, 3, ref written);
            result.AppendLiteral(chunk, written);
        }
        return move result;
    }

    public static String Decode(immutable(char*) text, nuint count)
    {
        nuint length = 0;
        if (!PercentEncoding::TryGetDecodedLength(text, count, ref length)) throw new Exception("Invalid percent encoding");
        String result = new String();
        result.Reserve(length);
        char[1] chunk;
        nuint index = 0;
        while (index < count)
        {
            nuint amount = 1;
            if (text[index] == '%') amount = 3;
            nuint written = 0;
            PercentEncoding::TryDecode(text + index, amount, chunk, 1, ref written);
            result.AppendLiteral(chunk, written);
            index += amount;
        }
        return move result;
    }
}
