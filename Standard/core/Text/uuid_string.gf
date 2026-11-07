namespace Standard::Text;

public String ToString(ref immutable(Uuid) value, char format)
{
    char[38] buffer; nuint written = 0;
    if (!value.TryFormat(buffer, 38, ref written, format)) throw new Exception("Unsupported UUID format");
    return String::From(buffer, written);
}

extension Uuid
{
    public immutable String ToString(char format) => Standard::Text::ToString(ref *this, format);
}
