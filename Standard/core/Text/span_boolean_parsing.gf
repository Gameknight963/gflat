using Standard::Memory;
namespace Standard::Text;

// Optional counted view adapter; parsing and failure policy stay in the codec.
bool TryParseBool(Span<readonly(char)> text, bool* result) nothrow
=> text.TryParseBool(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseBool(bool* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseBool("", 0, result);
        return Standard::Text::TryParseBool(protectedView.Data!, protectedView.Length, result);
    }
}
