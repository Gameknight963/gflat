using std::Memory;
namespace std::Text;

TextLines Lines(this Span<readonly(char)> text) => new TextLines(text);
TextLines Lines(this Span<char> text) => new TextLines(text.AsImmutable());
