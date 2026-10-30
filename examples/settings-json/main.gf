using Standard::Text;
using Standard::Json;
using Standard::Memory;
using Standard::Collections;
using Standard;

void Report(nuint line, readonly(char)* message)
{
    Console::Error.Write("Line ", 5);
    line.Format(Console::Error);
    Console::Error.Write(": ", 2);
    Console::Error.WriteLine(message);
}

int main()
{
    nuint lineNumber = 0;
    try
    {
        HashMap<String, String> settings = new();
        char[4096] buffer;
        nuint length = 0;
        while (Console::TryReadLine(buffer, 4096, ref length))
        {
            if (lineNumber == (nuint)0 - (nuint)1) throw new Exception("Line count overflow");
            lineNumber++;
            Span<readonly(char)> line = new(buffer, length);
            line = line.Trim();
            if (line.IsEmpty || line[0] == '#') continue;
            Span<readonly(char)> key = new();
            Span<readonly(char)> value = new();
            if (!line.TrySplitOnce('=', ref key, ref value))
            { Report(lineNumber, "Expected key=value"); return 2; }
            key = key.Trim(); value = value.Trim();
            if (key.IsEmpty) { Report(lineNumber, "Key must not be empty"); return 2; }
            nuint scalars = 0;
            if (!Utf8::TryCount(key.Data!, key.Length, &scalars)
                || !Utf8::TryCount(value.Data!, value.Length, &scalars))
            { Report(lineNumber, "Expected valid UTF-8"); return 2; }
            if (!settings.TryAdd(String::From(key.Data!, key.Length), String::From(value.Data!, value.Length)))
            { Report(lineNumber, "Duplicate key"); return 2; }
        }
        JsonWriter json = new(Console::Out, true);
        json.WriteStartObject();
        HashMap<String, String>::Enumerator iterator = settings.GetEnumerator();
        while (iterator.MoveNext())
        {
            immutable(String)* key = iterator.Current.Key;
            immutable(String)* value = iterator.Current.ValueImmutable;
            json.WritePropertyName(key.Data, key.Length);
            json.WriteString(value.Data, value.Length);
        }
        json.WriteEndObject();
        Console::WriteLine(); Console::Flush();
    }
    catch (ref BufferTooSmallException error)
    {
        if (lineNumber == (nuint)0 - (nuint)1) Console::Error.WriteLine("Line count overflow");
        else Report(lineNumber + 1, "Line exceeds the 4095-byte payload limit");
        return 2;
    }
    catch (ref Exception error)
    { Console::Error.WriteLine(error.GetMessage()); return 1; }
    return 0;
}
