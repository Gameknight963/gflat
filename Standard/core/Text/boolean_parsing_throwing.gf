namespace Standard::Text;

// Optional wrappers requiring the normal exception definitions and runtime.
bool ParseBool(readonly(char)* text, nuint count)
{
    bool result = false;
    if (!TryParseBool(text, count, &result)) throw new Exception("Invalid boolean text");
    return result;
}

bool ParseBool(readonly(char)* text)
{
    nuint count = 0;
    while (text[count] != '\0') count++;
    return ParseBool(text, count);
}
