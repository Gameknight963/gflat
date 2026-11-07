using Standard::Memory;

namespace Standard::Text;

public TextPartsPattern Split(Span<readonly(char)> text, Span<readonly(char)> separator, bool removeEmpty = false)
    => new TextPartsPattern(text, separator, removeEmpty);

extension Span<readonly(char)>
{
    public immutable TextPartsPattern Split(Span<readonly(char)> separator, bool removeEmpty = false)
        => Standard::Text::Split(*this, separator, removeEmpty);
}

public TextPartsPattern Split(Span<readonly(char)> text, readonly(char)* separator, nuint count, bool removeEmpty = false)
    => new TextPartsPattern(text, new Span<readonly(char)>(separator, count), removeEmpty);

extension Span<readonly(char)>
{
    public immutable TextPartsPattern Split(readonly(char)* separator, nuint count, bool removeEmpty = false)
        => Standard::Text::Split(*this, separator, count, removeEmpty);
}

public TextPartsPattern Split(Span<readonly(char)> text, readonly(char)* separator, bool removeEmpty = false)
    => new TextPartsPattern(text, new Span<readonly(char)>(separator, CString::Length(separator)), removeEmpty);

extension Span<readonly(char)>
{
    public immutable TextPartsPattern Split(readonly(char)* separator, bool removeEmpty = false)
        => Standard::Text::Split(*this, separator, removeEmpty);
}
