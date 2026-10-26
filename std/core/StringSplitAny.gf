using std::Memory;
namespace std;

TextPartsAny SplitAny(this ref immutable(String) text, Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny(new Span<readonly(char)>(text.Data, text.Length), separators, removeEmpty);
TextPartsAny SplitAny(this ref immutable(String) text, readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny(new Span<readonly(char)>(text.Data, text.Length), separators, removeEmpty);
