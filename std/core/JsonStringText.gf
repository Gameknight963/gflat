using std::Memory;
using std::Collections;
namespace std;

// Owning conveniences, separate from the allocation-free JSON string codec.
public static class JsonStringText
{
    public static String Quote(Span<readonly(char)> text)
    {
        if (text.IsEmpty) return JsonStringText::Quote("", 0);
        return JsonStringText::Quote(text.Data!, text.Length);
    }

    public static String Unquote(Span<readonly(char)> text)
    {
        if (text.IsEmpty) return JsonStringText::Unquote("", 0);
        return JsonStringText::Unquote(text.Data!, text.Length);
    }

    public static String Quote(readonly(char)* data, nuint count)
    {
        nuint required = 0;
        if (!JsonString::TryGetQuotedLength(data, count, ref required))
            throw new Exception("Invalid UTF-8 or JSON string length overflow");
        String result = new();
        result.Resize(required);
        Span<char> output = result.AsSpan();
        nuint written = 0;
        if (!JsonString::TryQuote(data, count, output.Data!, required, ref written))
            throw new Exception("JSON string quoting failed");
        return move result;
    }

    public static String Unquote(readonly(char)* text, nuint count)
    {
        nuint required = 0;
        if (!JsonString::TryGetUnquotedLength(text, count, ref required))
            throw new Exception("Invalid JSON string literal");
        String result = new();
        if (required == 0) return move result;
        result.Resize(required);
        Span<char> output = result.AsSpan();
        nuint written = 0;
        if (!JsonString::TryUnquote(text, count, output.Data!, required, ref written))
            throw new Exception("JSON string unquoting failed");
        return move result;
    }
}
