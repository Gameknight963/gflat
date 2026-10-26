using std::Memory;
namespace std::Text;

// Optional throwing adapter; include Text/boolean_parsing_throwing.gf as well.
bool ParseBool(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseBool("", 0);
    return std::Text::ParseBool(text.Data!, text.Length);
}
