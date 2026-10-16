using gflat.CompileExceptions;

namespace gflat;

internal static class CharacterLiteralEncoding
{
    public static byte Value(string text, int line = 0)
    {
        if (text.Length < 3 || text[0] != '\'' || text[^1] != '\'')
            throw new TypeCheckException("Invalid character literal", line);
        string inner = text[1..^1];
        char value;
        if (inner.Length == 1 && inner[0] != '\\' && inner[0] is not '\r' and not '\n') value = inner[0];
        else if (inner.Length == 2 && inner[0] == '\\')
            value = inner[1] switch
            {
                'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0',
                '\\' => '\\', '\'' => '\'', '"' => '"',
                _ => throw new TypeCheckException($"Unsupported character escape '\\{inner[1]}'", line)
            };
        else throw new TypeCheckException("Invalid character literal", line);
        if (value > byte.MaxValue)
            throw new TypeCheckException("Character literal does not fit in an 8-bit char", line);
        return (byte)value;
    }
}
