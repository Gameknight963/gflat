namespace Console;

// Borrow the existing String buffer rather than copying an owning value.
void Write(ref immutable(std::Text::String) message) { Write(message.Data, message.Length); }
void WriteLine(ref immutable(std::Text::String) message) { Write(message.Data, message.Length); WriteLine(); }

bool TryReadLine(ref std::Text::String line)
{
    line.Clear();
    return detail::ReadLine(&line);
}

std::Text::String ReadLine()
{
    std::Text::String line = new std::Text::String();
    if (!TryReadLine(ref line)) throw new std::IO::EndOfStreamException();
    return move line;
}
