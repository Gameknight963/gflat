namespace std::Text;

// Borrowed writer conveniences. LF is used on every platform; no flush is implied.
public void WriteLine(ITextWriter* writer)
{
    writer.Write("\n", 1);
}

extension ITextWriter
{
    public void WriteLine()
    => std::Text::WriteLine(this);
}

public void WriteLine(ITextWriter* writer, readonly(char)* data, nuint length)
{
    writer.Write(data, length);
    writer.Write("\n", 1);
}

extension ITextWriter
{
    public void WriteLine(readonly(char)* data, nuint length)
    => std::Text::WriteLine(this, data, length);
}

public void WriteLine(ITextWriter* writer, readonly(char)* text)
{
    nuint length = 0;
    while (text[length] != '\0') length++;
    std::Text::WriteLine(writer, text, length);
}

extension ITextWriter
{
    public void WriteLine(readonly(char)* text)
    => std::Text::WriteLine(this, text);
}

