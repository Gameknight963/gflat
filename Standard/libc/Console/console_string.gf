namespace Console;

// Borrow the existing String buffer rather than copying an owning value.
void Write(ref immutable(Standard::Text::String) message) { Write(message.Data, message.Length); }
void WriteLine(ref immutable(Standard::Text::String) message) { Write(message.Data, message.Length); WriteLine(); }

bool TryReadLine(ref Standard::Text::String line)
{
    line.Clear();
    return detail::ReadLine(&line);
}

Standard::Text::String ReadLine()
{
    Standard::Text::String line = new Standard::Text::String();
    if (!TryReadLine(ref line)) throw new Standard::IO::EndOfStreamException();
    return move line;
}
