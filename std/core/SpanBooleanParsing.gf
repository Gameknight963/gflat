using std::Memory;
namespace std;

// Optional counted view adapter; parsing and failure policy stay in the codec.
bool TryParseBool(this Span<readonly(char)> text, bool* result) nothrow
{
    if (text.IsEmpty) return std::TryParseBool("", 0, result);
    return std::TryParseBool(text.Data!, text.Length, result);
}
