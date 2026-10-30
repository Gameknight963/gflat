namespace Standard;

namespace detail
{
    void IntegerText(ulong magnitude, bool negative, ITextWriter* output)
    {
        char[21] buffer;
        nuint start = 21;
        while (true)
        {
            start--;
            buffer[start] = (char)((magnitude % 10ul) + 48ul);
            magnitude /= 10ul;
            if (magnitude == 0ul) break;
        }
        if (negative) { start--; buffer[start] = '-'; }
        output.Write((readonly(char)*)buffer + start, 21 - start);
    }
}

void Format(ulong value, ITextWriter* output) => value.Format(output);

extension ulong
{
    public immutable void Format(ITextWriter* output)
    { detail::IntegerText((*this), false, output); }
}
void Format(long value, ITextWriter* output)
=> value.Format(output);

extension long
{
    public immutable void Format(ITextWriter* output)
    {
        if ((*this) < 0l) detail::IntegerText(0ul - (ulong)(*this), true, output);
        else detail::IntegerText((ulong)(*this), false, output);
    }
}
void Format(int value, ITextWriter* output) => value.Format(output);

extension int
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((long)(*this), output); }
}
void Format(uint value, ITextWriter* output) => value.Format(output);

extension uint
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((ulong)(*this), output); }
}
void Format(short value, ITextWriter* output) => value.Format(output);

extension short
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((long)(*this), output); }
}
void Format(ushort value, ITextWriter* output) => value.Format(output);

extension ushort
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((ulong)(*this), output); }
}
void Format(sbyte value, ITextWriter* output) => value.Format(output);

extension sbyte
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((long)(*this), output); }
}
void Format(byte value, ITextWriter* output) => value.Format(output);

extension byte
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((ulong)(*this), output); }
}
void Format(nint value, ITextWriter* output) => value.Format(output);

extension nint
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((long)(*this), output); }
}
void Format(nuint value, ITextWriter* output) => value.Format(output);

extension nuint
{
    public immutable void Format(ITextWriter* output)
    { Standard::Format((ulong)(*this), output); }
}
void Format(bool value, ITextWriter* output)
=> value.Format(output);

extension bool
{
    public immutable void Format(ITextWriter* output)
    {
        if ((*this)) output.Write("true", 4);
        else output.Write("false", 5);
    }
}
void Format(char value, ITextWriter* output) => value.Format(output);

extension char
{
    public immutable void Format(ITextWriter* output)
    { output.Write(&(*this), 1); }
}
