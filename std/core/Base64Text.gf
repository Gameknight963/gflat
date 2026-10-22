namespace std;

// Owning String conveniences, kept separate from the caller-buffer codec.
public static class Base64Text
{
    public static String Encode(immutable(char*) data, nuint count)
    {
        nuint length = 0;
        if (!Base64::TryGetEncodedLength(count, ref length)) throw new Exception("Base64 length overflow");
        String result = new String();
        result.Reserve(length);
        char[4] chunk;
        nuint index = 0;
        while (index < count)
        {
            nuint amount = count - index;
            if (amount > 3) amount = 3;
            nuint written = 0;
            Base64::TryEncode(data + index, amount, chunk, 4, ref written);
            result.AppendLiteral(chunk, written);
            index += amount;
        }
        return move result;
    }

    public static String Decode(immutable(char*) text, nuint count)
    {
        nuint length = 0;
        if (!Base64::TryGetDecodedLength(text, count, ref length)) throw new Exception("Invalid Base64");
        String result = new String();
        result.Reserve(length);
        char[3] chunk;
        for (nuint index = 0; index < count; index += 4)
        {
            nuint written = 0;
            Base64::TryDecode(text + index, 4, chunk, 3, ref written);
            result.AppendLiteral(chunk, written);
        }
        return move result;
    }
}
