using std::Memory;
namespace std::Text;

TextPartsAny SplitAny(ref immutable(String) text, Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny(new Span<readonly(char)>(text.Data, text.Length), separators, removeEmpty);

extension String
{
    public immutable TextPartsAny SplitAny(Span<readonly(char)> separators, bool removeEmpty = false)
    => std::Text::SplitAny(ref *this, separators, removeEmpty);
}
TextPartsAny SplitAny(ref immutable(String) text, readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny(new Span<readonly(char)>(text.Data, text.Length), separators, removeEmpty);

extension String
{
    public immutable TextPartsAny SplitAny(readonly(char)* separators, bool removeEmpty = false)
    => std::Text::SplitAny(ref *this, separators, removeEmpty);
}
