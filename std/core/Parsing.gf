namespace std;

// Parse exactly count bytes as a signed decimal int. No whitespace is skipped.
// Returns false and writes zero on empty input, invalid text, or overflow.
// text must point to count readable bytes; result must point to writable storage.
bool TryParseInt(readonly(char)* text, nuint count, int* result)
{
    *result = 0;
    if (count == (nuint)0) return false;

    nuint index = 0;
    bool negative = text[0] == '-';
    if (negative || text[0] == '+') index++;
    if (index == count) return false;

    long limit = 2147483647l;
    if (negative) limit = 2147483648l;
    long magnitude = 0;
    for (; index < count; index++)
    {
        char character = text[index];
        if (character < '0' || character > '9') return false;
        long digit = (long)(character - '0');
        if (magnitude > (limit - digit) / 10l) return false;
        magnitude = magnitude * 10l + digit;
    }

    if (negative) *result = (int)(-magnitude);
    else *result = (int)magnitude;
    return true;
}

// Convenience overload for null-terminated input.
bool TryParseInt(readonly(char)* text, int* result)
{
    nuint count = 0;
    while (text[count] != '\0') count++;
    return TryParseInt(text, count, result);
}

// Same grammar as TryParseInt; invalid input and overflow throw Exception.
int ParseInt(readonly(char)* text, nuint count)
{
    int result = 0;
    if (!TryParseInt(text, count, &result))
        throw new Exception(c"Invalid or out-of-range decimal int");
    return result;
}

int ParseInt(readonly(char)* text)
{
    nuint count = 0;
    while (text[count] != '\0') count++;
    return ParseInt(text, count);
}
