using std::Memory;
namespace std;

// Trim ASCII whitespace by slicing the borrowed view, never changing its bytes.
Span<char> TrimStart(this Span<char> text)
{
    nuint start = 0;
    while (start < text.Length && Ascii::IsWhiteSpace(text[start])) start++;
    return text.Slice(start, text.Length - start);
}

Span<char> TrimEnd(this Span<char> text)
{
    nuint end = text.Length;
    while (end != 0 && Ascii::IsWhiteSpace(text[end - 1])) end--;
    return text.Slice(0, end);
}

Span<char> Trim(this Span<char> text) => text.TrimEnd().TrimStart();

Span<readonly(char)> TrimStart(this Span<readonly(char)> text)
{
    nuint start = 0;
    while (start < text.Length && Ascii::IsWhiteSpace(text[start])) start++;
    return text.Slice(start, text.Length - start);
}

Span<readonly(char)> TrimEnd(this Span<readonly(char)> text)
{
    nuint end = text.Length;
    while (end != 0 && Ascii::IsWhiteSpace(text[end - 1])) end--;
    return text.Slice(0, end);
}

Span<readonly(char)> Trim(this Span<readonly(char)> text) => text.TrimEnd().TrimStart();
