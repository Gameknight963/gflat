namespace Standard::Json;

public static class JsonText
{
    // Input stays live and unchanged, and must not alias output's mutable storage.
    public static void Minify(readonly(char)* input, nuint count, ITextWriter* output)
    {
        // Complete validation, including stack cleanup, precedes every output call.
        {
            JsonReader reader = new(input, count);
            while (reader.Read()) {}
        }

        nuint start = 0;
        bool quoted = false;
        bool escaped = false;
        for (nuint i = 0; i < count; i++)
        {
            char value = input[i];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (value == '\\') escaped = true;
                else if (value == '"') quoted = false;
            }
            else if (value == '"') quoted = true;
            else if (value == ' ' || value == '\t' || value == '\r' || value == '\n')
            {
                if (i > start) output.Write(input + start, i - start);
                start = i + 1;
            }
        }
        if (start < count) output.Write(input + start, count - start);
    }

    private static bool PrettyWhitespace(char value) nothrow
        => value == ' ' || value == '\t' || value == '\r' || value == '\n';

    private static void PrettyIndent(ITextWriter* output, readonly(char)* spaces, nuint amount)
    {
        output.Write("\n", 1);
        while (amount != 0)
        {
            nuint chunk = amount;
            if (chunk > 64) chunk = 64;
            output.Write(spaces, chunk);
            amount -= chunk;
        }
    }

    // Input remains live, unchanged, and separate from destination storage/state.
    public static void PrettyPrint(readonly(char)* input, nuint count,
        ITextWriter* output, nuint indentSpaces = 2)
    {
        nuint maximumDepth = 0;
        {
            JsonReader reader = new(input, count);
            while (reader.Read())
                if (reader.Depth > maximumDepth) maximumDepth = reader.Depth;
        }
        // Every emitted indentation matches a token depth; empty interiors need none.
        if (maximumDepth != 0 && indentSpaces > ((nuint)0 - (nuint)1) / maximumDepth)
            throw new Exception("JSON indentation length overflow");

        char[64] spaces;
        for (nuint i = 0; i < 64; i++) spaces[i] = ' ';
        nuint position = 0;
        nuint depth = 0;
        char previous = '\0';
        while (position < count)
        {
            char value = input[position];
            if (PrettyWhitespace(value)) { position++; continue; }
            bool closing = value == '}' || value == ']';
            if (closing)
            {
                depth--;
                if (previous != '{' && previous != '[')
                    PrettyIndent(output, spaces, depth * indentSpaces);
            }
            else if (previous == '{' || previous == '[')
                PrettyIndent(output, spaces, depth * indentSpaces);

            if (value == '{' || value == '[' || closing)
            {
                output.Write(input + position, 1);
                position++;
                if (!closing) depth++;
            }
            else if (value == ',')
            {
                output.Write(input + position, 1);
                position++;
                PrettyIndent(output, spaces, depth * indentSpaces);
            }
            else if (value == ':')
            {
                output.Write(": ", 2);
                position++;
            }
            else
            {
                nuint start = position;
                if (value == '"')
                {
                    position++;
                    bool escaped = false;
                    while (position < count)
                    {
                        char current = input[position++];
                        if (!escaped && current == '"') break;
                        if (!escaped && current == '\\') escaped = true;
                        else escaped = false;
                    }
                }
                else
                {
                    while (position < count && !PrettyWhitespace(input[position])
                        && input[position] != ',' && input[position] != ']' && input[position] != '}')
                        position++;
                }
                output.Write(input + start, position - start);
            }
            previous = value;
        }
    }
}
