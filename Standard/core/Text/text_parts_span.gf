using Standard::Memory;
namespace Standard::Text;

TextParts Split(Span<readonly(char)> text, char delimiter, bool removeEmpty = false)
    => text.Split(delimiter, removeEmpty);

extension Span<readonly(char)>
{
    public immutable TextParts Split(char delimiter, bool removeEmpty = false)
    {
        Span<readonly(char)> protectedView = *this;
        return new TextParts(protectedView, delimiter, removeEmpty);
    }
}

TextParts Split(Span<char> text, char delimiter, bool removeEmpty = false)
    => text.Split(delimiter, removeEmpty);

extension Span<char>
{
    public immutable TextParts Split(char delimiter, bool removeEmpty = false)
    => new TextParts((*this).AsImmutable(), delimiter, removeEmpty);
}
