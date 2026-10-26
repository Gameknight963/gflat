using std::Memory;
namespace std;

TextParts Split(this Span<readonly(char)> text, char delimiter, bool removeEmpty = false)
    => new TextParts(text, delimiter, removeEmpty);

TextParts Split(this Span<char> text, char delimiter, bool removeEmpty = false)
    => new TextParts(text.AsImmutable(), delimiter, removeEmpty);
