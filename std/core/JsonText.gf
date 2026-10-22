namespace std;

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
}
