using std::Memory;
namespace std;

TextPartsAny SplitAny(this Span<readonly(char)> text, Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny(text, separators, removeEmpty);
TextPartsAny SplitAny(this Span<char> text, Span<readonly(char)> separators, bool removeEmpty = false)
    => new TextPartsAny(text.AsImmutable(), separators, removeEmpty);
TextPartsAny SplitAny(this Span<readonly(char)> text, readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny(text, separators, removeEmpty);
TextPartsAny SplitAny(this Span<char> text, readonly(char)* separators, bool removeEmpty = false)
    => new TextPartsAny(text.AsImmutable(), separators, removeEmpty);
