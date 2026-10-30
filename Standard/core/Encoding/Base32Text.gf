using Standard::Text;
namespace Standard::Encoding;

// Owning conveniences, separate from the allocation-free Base32 codec.
// Input remains readable and unchanged for the duration of each call.
public static class Base32Text
{
    public static String Encode(readonly(char)* data, nuint count)
    {
        nuint length = 0;
        if (!Base32::TryGetEncodedLength(count, ref length))
            throw new Exception("Base32 length overflow");
        String result = new String();
        result.Reserve(length);
        char[8] chunk;
        nuint index = 0;
        while (index < count)
        {
            nuint amount = count - index;
            if (amount > 5) amount = 5;
            nuint written = 0;
            Base32::TryEncode(data + index, amount, chunk, 8, ref written);
            result.AppendLiteral(chunk, written);
            index += amount;
        }
        return move result;
    }

    public static String Decode(readonly(char)* text, nuint count)
    {
        nuint length = 0;
        if (!Base32::TryGetDecodedLength(text, count, ref length))
            throw new Exception("Invalid Base32");
        String result = new String();
        result.Reserve(length);
        char[5] chunk;
        for (nuint index = 0; index < count; index += 8)
        {
            nuint written = 0;
            Base32::TryDecode(text + index, 8, chunk, 5, ref written);
            result.AppendLiteral(chunk, written);
        }
        return move result;
    }
}
