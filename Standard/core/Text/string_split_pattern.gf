using Standard::Memory;

namespace Standard::Text;

public TextPartsPattern Split(ref immutable(String) text, Span<readonly(char)> separator, bool removeEmpty = false)
    => new TextPartsPattern(new Span<readonly(char)>(text.Data, text.Length), separator, removeEmpty);

extension String
{
    public immutable TextPartsPattern Split(Span<readonly(char)> separator, bool removeEmpty = false)
        => Standard::Text::Split(ref *this, separator, removeEmpty);
}

public TextPartsPattern Split(ref immutable(String) text, readonly(char)* separator, nuint count, bool removeEmpty = false)
    => new TextPartsPattern(new Span<readonly(char)>(text.Data, text.Length), new Span<readonly(char)>(separator, count), removeEmpty);

extension String
{
    public immutable TextPartsPattern Split(readonly(char)* separator, nuint count, bool removeEmpty = false)
        => Standard::Text::Split(ref *this, separator, count, removeEmpty);
}

public TextPartsPattern Split(ref immutable(String) text, readonly(char)* separator, bool removeEmpty = false)
    => new TextPartsPattern(new Span<readonly(char)>(text.Data, text.Length), new Span<readonly(char)>(separator, CString::Length(separator)), removeEmpty);

extension String
{
    public immutable TextPartsPattern Split(readonly(char)* separator, bool removeEmpty = false)
        => Standard::Text::Split(ref *this, separator, removeEmpty);
}
