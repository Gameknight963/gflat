using Standard::Memory;
namespace Standard::Text;

TextPartsAny SplitAny(Span<readonly(char)> text, Span<readonly(char)> separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<readonly(char)>
{
    public immutable TextPartsAny SplitAny(Span<readonly(char)> separators, bool removeEmpty = false)
    {
        Span<readonly(char)> protectedView = *this;
        return new TextPartsAny(protectedView, separators, removeEmpty);
    }
}
TextPartsAny SplitAny(Span<char> text, Span<readonly(char)> separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<char>
{
    public immutable TextPartsAny SplitAny(Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny((*this).AsImmutable(), separators, removeEmpty);
}
TextPartsAny SplitAny(Span<readonly(char)> text, readonly(char)* separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<readonly(char)>
{
    public immutable TextPartsAny SplitAny(readonly(char)* separators, bool removeEmpty = false)
    {
        Span<readonly(char)> protectedView = *this;
        return new TextPartsAny(protectedView, separators, removeEmpty);
    }
}
TextPartsAny SplitAny(Span<char> text, readonly(char)* separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<char>
{
    public immutable TextPartsAny SplitAny(readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny((*this).AsImmutable(), separators, removeEmpty);
}
