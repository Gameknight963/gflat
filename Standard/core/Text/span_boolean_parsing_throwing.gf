using Standard::Memory;
namespace Standard::Text;

// Optional throwing adapter; include Text/boolean_parsing_throwing.gf as well.
bool ParseBool(Span<readonly(char)> text)
=> text.ParseBool();

extension Span<readonly(char)>
{
    public immutable bool ParseBool()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseBool("", 0);
        return Standard::Text::ParseBool(protectedView.Data!, protectedView.Length);
    }
}
