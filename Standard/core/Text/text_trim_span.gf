using Standard::Text;
using Standard::Memory;
namespace Standard::Text;

// Trim ASCII whitespace by slicing the borrowed view, never changing its bytes.
Span<char> TrimStart(Span<char> text)
=> text.TrimStart();

extension Span<char>
{
    public readonly Span<char> TrimStart()
    {
        nuint start = 0;
        while (start < (*this).Length && Ascii::IsWhiteSpace((*this)[start])) start++;
        return (*this).Slice(start, (*this).Length - start);
    }
}

Span<char> TrimEnd(Span<char> text)
=> text.TrimEnd();

extension Span<char>
{
    public readonly Span<char> TrimEnd()
    {
        nuint end = (*this).Length;
        while (end != 0 && Ascii::IsWhiteSpace((*this)[end - 1])) end--;
        return (*this).Slice(0, end);
    }
}

Span<char> Trim(Span<char> text) => text.Trim();

extension Span<char>
{
    public readonly Span<char> Trim()
    => (*this).TrimEnd().TrimStart();
}

Span<readonly(char)> TrimStart(Span<readonly(char)> text)
=> text.TrimStart();

extension Span<readonly(char)>
{
    public immutable Span<readonly(char)> TrimStart()
    {
        Span<readonly(char)> protectedView = *this;
        nuint start = 0;
        while (start < protectedView.Length && Ascii::IsWhiteSpace(protectedView[start])) start++;
        return protectedView.Slice(start, protectedView.Length - start);
    }
}

Span<readonly(char)> TrimEnd(Span<readonly(char)> text)
=> text.TrimEnd();

extension Span<readonly(char)>
{
    public immutable Span<readonly(char)> TrimEnd()
    {
        Span<readonly(char)> protectedView = *this;
        nuint end = protectedView.Length;
        while (end != 0 && Ascii::IsWhiteSpace(protectedView[end - 1])) end--;
        return protectedView.Slice(0, end);
    }
}

Span<readonly(char)> Trim(Span<readonly(char)> text) => text.Trim();

extension Span<readonly(char)>
{
    public immutable Span<readonly(char)> Trim()
    => (*this).TrimEnd().TrimStart();
}
