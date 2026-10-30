using Standard::Memory;
namespace Standard::Text;

TextLines Lines(Span<readonly(char)> text) => text.Lines();

extension Span<readonly(char)>
{
    public immutable TextLines Lines()
    {
        Span<readonly(char)> protectedView = *this;
        return new TextLines(protectedView);
    }
}
TextLines Lines(Span<char> text) => text.Lines();

extension Span<char>
{
    public immutable TextLines Lines()
    => new TextLines((*this).AsImmutable());
}
