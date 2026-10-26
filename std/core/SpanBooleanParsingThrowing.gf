using std::Memory;
namespace std;

// Optional throwing adapter; include BooleanParsingThrowing.gf as well.
bool ParseBool(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseBool("", 0);
    return std::ParseBool(text.Data!, text.Length);
}
