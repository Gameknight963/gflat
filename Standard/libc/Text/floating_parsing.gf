namespace Standard::Text;

namespace floating_parsing_detail
{
    private extern float strtof(readonly(char)* text, char** end) nothrow;
    private extern double strtod(readonly(char)* text, char** end) nothrow;
    private extern int isspace(int value) nothrow;
}

/// Parses a complete C string using the current C locale and native floating grammar.
/// Rejects empty input, surrounding whitespace and trailing bytes; failure writes positive zero.
/// Native range results, including infinity and underflow to signed zero, are successful conversions.
/// The wrapper makes no text copy or allocation; input must be terminated and output writable.
/// Input and output may overlap: output is committed only after all input reads finish.
bool TryParseFloat(readonly(char)* text, float* result) nothrow
{
    float value = 0.0f;
    bool valid = false;
    if (text[0] != '\0' && floating_parsing_detail::isspace((int)(byte)text[0]) == 0)
    {
        char* end = (char*)text;
        value = floating_parsing_detail::strtof(text, &end);
        valid = (readonly(char)*)end != text && end[0] == '\0';
    }
    if (!valid) value = 0.0f;
    *result = value;
    return valid;
}

/// Double-precision counterpart of TryParseFloat, with the same C-string and locale contract.
bool TryParseDouble(readonly(char)* text, double* result) nothrow
{
    double value = 0.0;
    bool valid = false;
    if (text[0] != '\0' && floating_parsing_detail::isspace((int)(byte)text[0]) == 0)
    {
        char* end = (char*)text;
        value = floating_parsing_detail::strtod(text, &end);
        valid = (readonly(char)*)end != text && end[0] == '\0';
    }
    if (!valid) value = 0.0;
    *result = value;
    return valid;
}
