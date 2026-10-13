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

void Format(ulong value, ITextWriter* output) { detail::IntegerText(value, false, output); }
void Format(long value, ITextWriter* output)
{
    if (value < 0l) detail::IntegerText(0ul - (ulong)value, true, output);
    else detail::IntegerText((ulong)value, false, output);
}
void Format(int value, ITextWriter* output) { std::Format((long)value, output); }
void Format(uint value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(short value, ITextWriter* output) { std::Format((long)value, output); }
void Format(ushort value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(sbyte value, ITextWriter* output) { std::Format((long)value, output); }
void Format(byte value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(nint value, ITextWriter* output) { std::Format((long)value, output); }
void Format(nuint value, ITextWriter* output) { std::Format((ulong)value, output); }
void Format(bool value, ITextWriter* output)
{
    if (value) output.Write(c"true", 4);
    else output.Write(c"false", 5);
}
void Format(char value, ITextWriter* output) { output.Write(&value, 1); }
