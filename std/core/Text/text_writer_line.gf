namespace std::Text;

// Borrowed writer conveniences. LF is used on every platform; no flush is implied.
public void WriteLine(this ITextWriter* writer)
{
    writer.Write("\n", 1);
}

public void WriteLine(this ITextWriter* writer, readonly(char)* data, nuint length)
{
    writer.Write(data, length);
    writer.Write("\n", 1);
}

public void WriteLine(this ITextWriter* writer, readonly(char)* text)
{
    nuint length = 0;
    while (text[length] != '\0') length++;
    std::Text::WriteLine(writer, text, length);
}

