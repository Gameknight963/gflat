namespace std::Ascii;

// char stores one byte. These operations classify ASCII, never UTF-8 scalars.
bool IsAscii(char value) => (byte)value < (byte)128;
bool IsDigit(char value) => (byte)value >= (byte)48 && (byte)value <= (byte)57;
bool IsLower(char value) => (byte)value >= (byte)97 && (byte)value <= (byte)122;
bool IsUpper(char value) => (byte)value >= (byte)65 && (byte)value <= (byte)90;
bool IsLetter(char value) => IsLower(value) || IsUpper(value);
bool IsLetterOrDigit(char value) => IsLetter(value) || IsDigit(value);
bool IsHexDigit(char value)
    => IsDigit(value) || ((byte)value >= (byte)65 && (byte)value <= (byte)70)
        || ((byte)value >= (byte)97 && (byte)value <= (byte)102);

// ASCII space, horizontal tab, line feed, vertical tab, form feed, carriage return.
bool IsWhiteSpace(char value)
    => value == ' ' || ((byte)value >= (byte)9 && (byte)value <= (byte)13);
bool IsControl(char value) => (byte)value < (byte)32 || (byte)value == (byte)127;
bool IsPrintable(char value) => (byte)value >= (byte)32 && (byte)value <= (byte)126;

// Case conversion leaves non-ASCII bytes and non-letter ASCII unchanged.
char ToLower(char value)
{
    if (IsUpper(value)) return (char)((uint)(byte)value + 32u);
    return value;
}

char ToUpper(char value)
{
    if (IsLower(value)) return (char)((uint)(byte)value - 32u);
    return value;
}

// Returns a value in [0, 15]; failure writes zero.
bool TryHexValue(char value, byte* result)
{
    *result = (byte)0;
    if (IsDigit(value)) { *result = (byte)((uint)(byte)value - 48u); return true; }
    if ((byte)value >= (byte)65 && (byte)value <= (byte)70)
    { *result = (byte)((uint)(byte)value - 55u); return true; }
    if ((byte)value >= (byte)97 && (byte)value <= (byte)102)
    { *result = (byte)((uint)(byte)value - 87u); return true; }
    return false;
}
