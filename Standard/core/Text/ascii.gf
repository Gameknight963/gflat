namespace Standard::Text::Ascii;

// char stores one byte. These operations classify ASCII, never UTF-8 scalars.
bool IsAscii(char value) nothrow => (byte)value < (byte)128;
bool IsDigit(char value) nothrow => (byte)value >= (byte)48 && (byte)value <= (byte)57;
bool IsLower(char value) nothrow => (byte)value >= (byte)97 && (byte)value <= (byte)122;
bool IsUpper(char value) nothrow => (byte)value >= (byte)65 && (byte)value <= (byte)90;
bool IsLetter(char value) nothrow => IsLower(value) || IsUpper(value);
bool IsLetterOrDigit(char value) nothrow => IsLetter(value) || IsDigit(value);
bool IsHexDigit(char value) nothrow
    => IsDigit(value) || ((byte)value >= (byte)65 && (byte)value <= (byte)70)
        || ((byte)value >= (byte)97 && (byte)value <= (byte)102);

// ASCII space, horizontal tab, line feed, vertical tab, form feed, carriage return.
bool IsWhiteSpace(char value) nothrow
    => value == ' ' || ((byte)value >= (byte)9 && (byte)value <= (byte)13);
bool IsControl(char value) nothrow => (byte)value < (byte)32 || (byte)value == (byte)127;
bool IsPrintable(char value) nothrow => (byte)value >= (byte)32 && (byte)value <= (byte)126;

// Case conversion leaves non-ASCII bytes and non-letter ASCII unchanged.
char ToLower(char value) nothrow
{
    if (IsUpper(value)) return (char)((uint)(byte)value + 32u);
    return value;
}

char ToUpper(char value) nothrow
{
    if (IsLower(value)) return (char)((uint)(byte)value - 32u);
    return value;
}

// Counted in-place conversion includes null bytes; zero count touches no storage.
void ToLower(char* data, nuint count) nothrow
{
    for (nuint i = 0; i < count; i++) data[i] = ToLower(data[i]);
}

void ToUpper(char* data, nuint count) nothrow
{
    for (nuint i = 0; i < count; i++) data[i] = ToUpper(data[i]);
}

// Returns a value in [0, 15]; failure writes zero.
bool TryHexValue(char value, byte* result) nothrow
{
    *result = (byte)0;
    if (IsDigit(value)) { *result = (byte)((uint)(byte)value - 48u); return true; }
    if ((byte)value >= (byte)65 && (byte)value <= (byte)70)
    { *result = (byte)((uint)(byte)value - 55u); return true; }
    if ((byte)value >= (byte)97 && (byte)value <= (byte)102)
    { *result = (byte)((uint)(byte)value - 87u); return true; }
    return false;
}

// Counted byte comparison, including embedded nulls. Only ASCII letters fold;
// all other bytes compare unchanged and unsigned. Results are -1, 0, or 1.
// A pointer need not designate storage when its corresponding length is zero.
int CompareIgnoreCase(readonly(char)* left, nuint leftLength,
    readonly(char)* right, nuint rightLength) nothrow
{
    nuint common = leftLength;
    if (rightLength < common) common = rightLength;
    for (nuint i = 0; i < common; i++)
    {
        byte a = (byte)ToUpper(left[i]);
        byte b = (byte)ToUpper(right[i]);
        if (a < b) return -1;
        if (a > b) return 1;
    }
    if (leftLength < rightLength) return -1;
    if (leftLength > rightLength) return 1;
    return 0;
}

bool EqualsIgnoreCase(readonly(char)* left, nuint leftLength,
    readonly(char)* right, nuint rightLength) nothrow
{
    if (leftLength != rightLength) return false;
    return CompareIgnoreCase(left, leftLength, right, rightLength) == 0;
}

// Counted ASCII-folded FNV-1a; equal folded byte sequences have equal hashes.
nuint HashIgnoreCase(readonly(char)* data, nuint count) nothrow
{
    uint hash = 2166136261u;
    for (nuint i = 0; i < count; i++)
        hash = (hash ^ (uint)(byte)ToUpper(data[i])) * 16777619u;
    return (nuint)hash;
}

// C-string overloads stop at the first null, with the same unsigned ASCII fold.
int CompareIgnoreCase(readonly(char)* left, readonly(char)* right) nothrow
{
    nuint i = 0;
    while (true)
    {
        byte a = (byte)ToUpper(left[i]);
        byte b = (byte)ToUpper(right[i]);
        if (a < b) return -1;
        if (a > b) return 1;
        if (a == (byte)0) return 0;
        i++;
    }
}

bool EqualsIgnoreCase(readonly(char)* left, readonly(char)* right) nothrow
    => CompareIgnoreCase(left, right) == 0;

// Counted ASCII-only substring search; embedded null bytes and non-ASCII bytes are compared as data.
// Empty patterns return zero; oversized patterns return length, without reading pointers.
// O(length*count) worst-case work and constant storage; neither input is modified.
nuint IndexOfIgnoreCase(readonly(char)* data, nuint length,
    readonly(char)* pattern, nuint count) nothrow
{
    if (count == 0) return 0;
    if (count > length) return length;
    nuint last = length - count;
    for (nuint start = 0; start <= last; start++)
        if (EqualsIgnoreCase(data + start, count, pattern, count)) return start;
    return length;
}

// Last matching start; an empty pattern returns length as the last insertion endpoint.
nuint LastIndexOfIgnoreCase(readonly(char)* data, nuint length,
    readonly(char)* pattern, nuint count) nothrow
{
    if (count == 0) return length;
    if (count > length) return length;
    nuint start = length - count;
    while (true)
    {
        if (EqualsIgnoreCase(data + start, count, pattern, count)) return start;
        if (start == 0) return length;
        start--;
    }
}
