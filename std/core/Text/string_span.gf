using std::Memory;
namespace std::Text;

// Views borrow the current counted bytes, never the terminator or spare capacity.
public Span<char> AsSpan(ref String value)
{
    if (value.Length == 0) return new Span<char>();
    return new Span<char>((char*)value.Data, value.Length);
}

extension String
{
    public Span<char> AsSpan()
    => std::Text::AsSpan(ref *this);
}

public Span<readonly(char)> AsReadOnly(ref readonly(String) value)
{
    if (value.Length == 0) return new Span<readonly(char)>();
    return new Span<readonly(char)>(value.Data, value.Length);
}

extension String
{
    public readonly Span<readonly(char)> AsReadOnly()
    => std::Text::AsReadOnly(ref *this);
}

public Span<readonly(char)> AsImmutable(ref immutable(String) value)
{
    if (value.Length == 0) return new Span<readonly(char)>();
    return new Span<readonly(char)>(value.Data, value.Length);
}

extension String
{
    public immutable Span<readonly(char)> AsImmutable()
    => std::Text::AsImmutable(ref *this);
}
