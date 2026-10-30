using std::Text;
using std::Memory;
namespace std::Text;

// Trim ASCII whitespace by slicing the borrowed view, never changing its bytes.
Span<char> TrimStart(Span<char> text)
=> text.TrimStart();

extension Span<char>
{
    public Span<char> TrimStart()
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
    public Span<char> TrimEnd()
    {
        nuint end = (*this).Length;
        while (end != 0 && Ascii::IsWhiteSpace((*this)[end - 1])) end--;
        return (*this).Slice(0, end);
    }
}

Span<char> Trim(Span<char> text) => text.Trim();

extension Span<char>
{
    public Span<char> Trim()
    => (*this).TrimEnd().TrimStart();
}

Span<readonly(char)> TrimStart(Span<readonly(char)> text)
=> text.TrimStart();

extension Span<readonly(char)>
{
    public Span<readonly(char)> TrimStart()
    {
        nuint start = 0;
        while (start < (*this).Length && Ascii::IsWhiteSpace((*this)[start])) start++;
        return (*this).Slice(start, (*this).Length - start);
    }
}

Span<readonly(char)> TrimEnd(Span<readonly(char)> text)
=> text.TrimEnd();

extension Span<readonly(char)>
{
    public Span<readonly(char)> TrimEnd()
    {
        nuint end = (*this).Length;
        while (end != 0 && Ascii::IsWhiteSpace((*this)[end - 1])) end--;
        return (*this).Slice(0, end);
    }
}

Span<readonly(char)> Trim(Span<readonly(char)> text) => text.Trim();

extension Span<readonly(char)>
{
    public Span<readonly(char)> Trim()
    => (*this).TrimEnd().TrimStart();
}
