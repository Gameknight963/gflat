namespace std::Text;

// ASCII true/false only, with arbitrary letter case and no surrounding bytes.
// Failure writes false. The input is read before committing overlapping output.
bool TryParseBool(readonly(char)* text, nuint count, bool* result) nothrow
{
    bool value = false;
    bool valid = false;
    if (count == 4)
    {
        value = (text[0] == 't' || text[0] == 'T') &&
            (text[1] == 'r' || text[1] == 'R') &&
            (text[2] == 'u' || text[2] == 'U') &&
            (text[3] == 'e' || text[3] == 'E');
        valid = value;
    }
    else if (count == 5)
    {
        valid = (text[0] == 'f' || text[0] == 'F') &&
            (text[1] == 'a' || text[1] == 'A') &&
            (text[2] == 'l' || text[2] == 'L') &&
            (text[3] == 's' || text[3] == 'S') &&
            (text[4] == 'e' || text[4] == 'E');
    }
    *result = value;
    return valid;
}

bool TryParseBool(readonly(char)* text, bool* result) nothrow
{
    nuint count = 0;
    while (text[count] != '\0') count++;
    return TryParseBool(text, count, result);
}
