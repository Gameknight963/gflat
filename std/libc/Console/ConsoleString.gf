namespace Console;

// Borrow the existing String buffer rather than copying an owning value.
void Write(ref readonly(std::String) message) { Write(message.Data, message.Length); }
void WriteLine(ref readonly(std::String) message) { Write(message.Data, message.Length); WriteLine(); }

bool TryReadLine(ref std::String line)
{
    line.Clear();
    return detail::ReadLine(&line);
}

std::String ReadLine()
{
    std::String line = new std::String();
    if (!TryReadLine(ref line)) throw new std::EndOfStreamException();
    return move line;
}
