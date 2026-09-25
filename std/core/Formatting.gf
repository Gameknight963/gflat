namespace std;

namespace detail
{
    char* AllocateText(nuint bytes) throws
    {
        void*? allocation = Allocator::Allocate(bytes);
        if (allocation == null) throw new* Exception(c"Text allocation failed", 2);
        return (char*)allocation;
    }

    char* CopyText(readonly(char)* text, nuint count) throws
    {
        char* result = AllocateText(count + (nuint)1);
        for (nuint i = 0; i < count; i++) result[i] = text[i];
        result[count] = '\0';
        return result;
    }

    char* IntegerText(ulong magnitude, bool negative) throws
    {
        // Twenty decimal digits, an optional sign, and the terminator.
        char* result = AllocateText(22);
        nuint length = 0;
        while (true)
        {
            result[length] = (char)((magnitude % 10ul) + 48ul);
            length++;
            magnitude /= 10ul;
            if (magnitude == 0ul) break;
        }
        if (negative) { result[length] = '-'; length++; }
        nuint i = 0;
        nuint j = length - (nuint)1;
        while (i < j)
        {
            char swap = result[i]; result[i] = result[j]; result[j] = swap;
            i++; j--;
        }
        result[length] = '\0';
        return result;
    }
}

char* ToString(ulong value) throws { return detail::IntegerText(value, false); }
char* ToString(long value) throws
{
    // Unsigned subtraction also handles the minimum signed value.
    if (value < 0l) return detail::IntegerText(0ul - (ulong)value, true);
    return detail::IntegerText((ulong)value, false);
}
char* ToString(int value) throws { return std::ToString((long)value); }
char* ToString(uint value) throws { return std::ToString((ulong)value); }
char* ToString(short value) throws { return std::ToString((long)value); }
char* ToString(ushort value) throws { return std::ToString((ulong)value); }
char* ToString(sbyte value) throws { return std::ToString((long)value); }
char* ToString(byte value) throws { return std::ToString((ulong)value); }
char* ToString(nint value) throws { return std::ToString((long)value); }
char* ToString(nuint value) throws { return std::ToString((ulong)value); }
char* ToString(bool value) throws
{
    if (value) return detail::CopyText(c"true", 4);
    return detail::CopyText(c"false", 5);
}
char* ToString(char value) throws
{
    char* result = detail::AllocateText(2);
    result[0] = value;
    result[1] = '\0';
    return result;
}
