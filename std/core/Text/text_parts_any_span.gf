using std::Memory;
namespace std::Text;

TextPartsAny SplitAny(Span<readonly(char)> text, Span<readonly(char)> separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<readonly(char)>
{
    public TextPartsAny SplitAny(Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny((*this), separators, removeEmpty);
}
TextPartsAny SplitAny(Span<char> text, Span<readonly(char)> separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<char>
{
    public TextPartsAny SplitAny(Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny((*this).AsImmutable(), separators, removeEmpty);
}
TextPartsAny SplitAny(Span<readonly(char)> text, readonly(char)* separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<readonly(char)>
{
    public TextPartsAny SplitAny(readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny((*this), separators, removeEmpty);
}
TextPartsAny SplitAny(Span<char> text, readonly(char)* separators, bool removeEmpty = false)
    => text.SplitAny(separators, removeEmpty);

extension Span<char>
{
    public TextPartsAny SplitAny(readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny((*this).AsImmutable(), separators, removeEmpty);
}
