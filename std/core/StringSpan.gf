namespace std;

// Views borrow the current counted bytes, never the terminator or spare capacity.
public Span<char> AsSpan(this ref String value)
{
    if (value.Length == 0) return new Span<char>();
    return new Span<char>((char*)value.Data, value.Length);
}

public Span<readonly(char)> AsReadOnly(this ref readonly(String) value)
{
    if (value.Length == 0) return new Span<readonly(char)>();
    return new Span<readonly(char)>(value.Data, value.Length);
}
