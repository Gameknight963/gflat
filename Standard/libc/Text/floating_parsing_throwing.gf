namespace Standard::Text;

// Parses using the current C locale, throwing on invalid syntax, whitespace, or trailing bytes.
// Native overflow and underflow results are accepted.
float ParseFloat(readonly(char)* text)
{
    float result = 0.0f;
    if (!TryParseFloat(text, &result)) throw new Exception("Invalid floating-point text");
    return result;
}

// Double-precision counterpart of ParseFloat, using the current C locale.
double ParseDouble(readonly(char)* text)
{
    double result = 0.0;
    if (!TryParseDouble(text, &result)) throw new Exception("Invalid floating-point text");
    return result;
}
