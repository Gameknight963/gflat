using Standard::Text;
namespace Standard::Encoding;

// Owning conveniences, separate from the allocation-free URL-safe codec.
// Input must remain readable and unchanged for the duration of the call.
public static class Base64UrlText
{
    public static String Encode(readonly(char)* data, nuint count)
    {
        nuint length = 0;
        if (!Base64Url::TryGetEncodedLength(count, ref length))
            throw new Exception("Base64Url length overflow");
        String result = new String();
        result.Reserve(length);
        char[4] chunk;
        nuint index = 0;
        while (index < count)
        {
            nuint amount = count - index;
            if (amount > 3) amount = 3;
            nuint written = 0;
            Base64Url::TryEncode(data + index, amount, chunk, 4, ref written);
            result.AppendLiteral(chunk, written);
            index += amount;
        }
        return move result;
    }

    public static String Decode(readonly(char)* text, nuint count)
    {
        nuint length = 0;
        if (!Base64Url::TryGetDecodedLength(text, count, ref length))
            throw new Exception("Invalid Base64Url");
        String result = new String();
        result.Reserve(length);
        char[3] chunk;
        nuint index = 0;
        while (index < count)
        {
            nuint amount = count - index;
            if (amount > 4) amount = 4;
            nuint written = 0;
            Base64Url::TryDecode(text + index, amount, chunk, 3, ref written);
            result.AppendLiteral(chunk, written);
            index += amount;
        }
        return move result;
    }
}
