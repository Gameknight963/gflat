using std::Memory;
namespace std::Text;

TextLines Lines(Span<readonly(char)> text) => text.Lines();

extension Span<readonly(char)>
{
    public TextLines Lines()
    => new TextLines((*this));
}
TextLines Lines(Span<char> text) => text.Lines();

extension Span<char>
{
    public TextLines Lines()
    => new TextLines((*this).AsImmutable());
}
