namespace Standard::Text;

public String ToString(ref immutable(Version) value, int fieldCount)
{
    char[43] buffer;
    nuint written = 0;
    if (!value.TryFormat(buffer, 43, ref written, fieldCount))
        throw new Exception("Version field count is unavailable");
    return String::From(buffer, written);
}

extension Version
{
    public immutable String ToString(int fieldCount)
        => Standard::Text::ToString(ref *this, fieldCount);
}
