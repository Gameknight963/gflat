using std::Memory;
namespace std::Text;

TextParts Split(Span<readonly(char)> text, char delimiter, bool removeEmpty = false)
    => text.Split(delimiter, removeEmpty);

extension Span<readonly(char)>
{
    public TextParts Split(char delimiter, bool removeEmpty = false)
    => new TextParts((*this), delimiter, removeEmpty);
}

TextParts Split(Span<char> text, char delimiter, bool removeEmpty = false)
    => text.Split(delimiter, removeEmpty);

extension Span<char>
{
    public TextParts Split(char delimiter, bool removeEmpty = false)
    => new TextParts((*this).AsImmutable(), delimiter, removeEmpty);
}
