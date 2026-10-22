using std;

void Decode(ref String scratch, readonly(char)* raw, nuint count)
{
    scratch.Clear();
    scratch.Append(raw, count);
    // Quoted tokens are nonempty: scratch owns writable storage. Neither decoding
    // nor shrinking grows it, and the cast stays within this exclusive borrow.
    char* data = (char*)scratch.Data;
    nuint decoded = 0;
    if (!JsonString::TryUnquote(data, count, data, count, ref decoded))
        throw new Exception("Validated JSON string could not be decoded");
    scratch.Remove(decoded, count - decoded);
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: json-minify <input-file>");
        return 2;
    }
    try
    {
        String document = FileText::ReadAll(argv[1]);
        JsonReader reader = new(document.Data, document.Length);
        // Validate the suffix too, before writing any stdout bytes.
        while (reader.Read()) { }
        reader.Reset();
        JsonWriter writer = new(Console::Out);
        String scratch = new();
        while (reader.Read())
        {
            JsonTokenKind kind = reader.Kind;
            readonly(char)* raw = document.Data + reader.TokenOffset;
            nuint count = reader.TokenLength;
            if (kind == JsonTokenKind::StartObject) writer.WriteStartObject();
            else if (kind == JsonTokenKind::EndObject) writer.WriteEndObject();
            else if (kind == JsonTokenKind::StartArray) writer.WriteStartArray();
            else if (kind == JsonTokenKind::EndArray) writer.WriteEndArray();
            else if (kind == JsonTokenKind::Number) writer.WriteRawNumber(raw, count);
            else if (kind == JsonTokenKind::True) writer.WriteBoolean(true);
            else if (kind == JsonTokenKind::False) writer.WriteBoolean(false);
            else if (kind == JsonTokenKind::Null) writer.WriteNull();
            else
            {
                Decode(ref scratch, raw, count);
                if (kind == JsonTokenKind::PropertyName) writer.WritePropertyName(scratch.Data, scratch.Length);
                else writer.WriteString(scratch.Data, scratch.Length);
            }
        }
        writer.Complete();
        Console::WriteLine();
        Console::Flush();
    }
    catch (ref JsonParseException error)
    {
        Console::Error.Write(argv[1], CString::Length(argv[1])); Console::Error.Write(": byte ", 7);
        error.Offset.Format(Console::Error);
        Console::Error.Write(": ", 2); Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    catch (ref Exception error)
    {
        Console::Error.Write(argv[1], CString::Length(argv[1])); Console::Error.Write(": ", 2);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
