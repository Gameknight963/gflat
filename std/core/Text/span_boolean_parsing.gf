using std::Memory;
namespace std::Text;

// Optional counted view adapter; parsing and failure policy stay in the codec.
bool TryParseBool(Span<readonly(char)> text, bool* result) nothrow
=> text.TryParseBool(result);

extension Span<readonly(char)>
{
    public bool TryParseBool(bool* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseBool("", 0, result);
        return std::Text::TryParseBool((*this).Data!, (*this).Length, result);
    }
}
