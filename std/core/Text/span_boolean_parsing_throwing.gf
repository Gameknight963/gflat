using std::Memory;
namespace std::Text;

// Optional throwing adapter; include Text/boolean_parsing_throwing.gf as well.
bool ParseBool(Span<readonly(char)> text)
=> text.ParseBool();

extension Span<readonly(char)>
{
    public bool ParseBool()
    {
        if ((*this).IsEmpty) return std::Text::ParseBool("", 0);
        return std::Text::ParseBool((*this).Data!, (*this).Length);
    }
}
