namespace std;

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
        output.Write((readonly(char)*)buffer + start, (nuint)21 - start);
    }
}

void Format(this ulong value, ITextWriter* output) { detail::IntegerText(value, false, output); }
void Format(this long value, ITextWriter* output)
{
    if (value < 0l) detail::IntegerText(0ul - (ulong)value, true, output);
    else detail::IntegerText((ulong)value, false, output);
}
void Format(this int value, ITextWriter* output) { std::Format((long)value, output); }
void Format(this uint value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(this short value, ITextWriter* output) { std::Format((long)value, output); }
void Format(this ushort value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(this sbyte value, ITextWriter* output) { std::Format((long)value, output); }
void Format(this byte value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(this nint value, ITextWriter* output) { std::Format((long)value, output); }
void Format(this nuint value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(this bool value, ITextWriter* output)
{
    if (value) output.Write("true", 4);
    else output.Write("false", 5);
}
void Format(this char value, ITextWriter* output) { output.Write(&value, 1); }
