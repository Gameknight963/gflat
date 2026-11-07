using Standard::Memory;

namespace Standard::Text;

// Views borrow the current counted bytes, never the terminator or spare capacity.
// Keep the owner alive and its storage stable; views do not extend a temporary lifetime.
// Explicit free ref overloads require existing storage; extension calls accept temporaries.
public Span<char> AsSpan(ref String value)
{
    if (value.Length == 0) return new Span<char>();
    return new Span<char>((char*)value.Data, value.Length);
}

extension String
{
    public Span<char> AsSpan()
    => Standard::Text::AsSpan(ref *this);
}

public Span<readonly(char)> AsReadOnly(ref readonly(String) value)
{
    if (value.Length == 0) return new Span<readonly(char)>();
    return new Span<readonly(char)>(value.Data, value.Length);
}

extension String
{
    public readonly Span<readonly(char)> AsReadOnly()
    => Standard::Text::AsReadOnly(ref *this);
}

public Span<readonly(char)> AsImmutable(ref immutable(String) value)
{
    if (value.Length == 0) return new Span<readonly(char)>();
    return new Span<readonly(char)>(value.Data, value.Length);
}

extension String
{
    public immutable Span<readonly(char)> AsImmutable()
    => Standard::Text::AsImmutable(ref *this);
}

public Span<char> AsSpan(ref String value, nuint start)
{
    Span<char> whole = Standard::Text::AsSpan(ref value);
    return whole.Slice(start);
}

extension String
{
    public Span<char> AsSpan(nuint start)
        => Standard::Text::AsSpan(ref *this, start);
}

public Span<char> AsSpan(ref String value, nuint start, nuint count)
{
    Span<char> whole = Standard::Text::AsSpan(ref value);
    return whole.Slice(start, count);
}

extension String
{
    public Span<char> AsSpan(nuint start, nuint count)
        => Standard::Text::AsSpan(ref *this, start, count);
}

public Span<readonly(char)> AsReadOnly(ref readonly(String) value, nuint start)
{
    Span<readonly(char)> whole = Standard::Text::AsReadOnly(ref value);
    return whole.Slice(start);
}

extension String
{
    public readonly Span<readonly(char)> AsReadOnly(nuint start)
        => Standard::Text::AsReadOnly(ref *this, start);
}

public Span<readonly(char)> AsReadOnly(ref readonly(String) value, nuint start, nuint count)
{
    Span<readonly(char)> whole = Standard::Text::AsReadOnly(ref value);
    return whole.Slice(start, count);
}

extension String
{
    public readonly Span<readonly(char)> AsReadOnly(nuint start, nuint count)
        => Standard::Text::AsReadOnly(ref *this, start, count);
}

public Span<readonly(char)> AsImmutable(ref immutable(String) value, nuint start)
{
    Span<readonly(char)> whole = Standard::Text::AsImmutable(ref value);
    return whole.Slice(start);
}

extension String
{
    public immutable Span<readonly(char)> AsImmutable(nuint start)
        => Standard::Text::AsImmutable(ref *this, start);
}

public Span<readonly(char)> AsImmutable(ref immutable(String) value, nuint start, nuint count)
{
    Span<readonly(char)> whole = Standard::Text::AsImmutable(ref value);
    return whole.Slice(start, count);
}

extension String
{
    public immutable Span<readonly(char)> AsImmutable(nuint start, nuint count)
        => Standard::Text::AsImmutable(ref *this, start, count);
}
