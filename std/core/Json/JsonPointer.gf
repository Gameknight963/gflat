using std::Text;
using std::Memory;
namespace std::Json;

// Borrowed RFC 6901 plain-form lookup. Storage must remain alive and unchanged.
public static class JsonPointer
{
    public static bool TryResolve(Span<readonly(char)> document,
        Span<readonly(char)> pointer, ref Span<readonly(char)> value)
    {
        readonly(char)* data = "";
        if (!document.IsEmpty) data = document.Data!;
        Span<readonly(char)> selected = new();
        {
            JsonReader reader = new(data, document.Length);
            reader.Read();
            nuint start = reader.TokenOffset;
            reader.Skip();
            selected = new(data + start, reader.TokenOffset + reader.TokenLength - start);
            while (reader.Read()) {}
        }
        readonly(char)* path = "";
        if (!pointer.IsEmpty) path = pointer.Data!;
        if (!Utf8::IsValid(path, pointer.Length)) return false;
        if (pointer.Length != 0 && path[0] != '/') return false;
        // Validate the entire pointer, including segments after a missing member.
        for (nuint i = 0; i < pointer.Length; i++)
        {
            if (path[i] != '~') continue;
            i++;
            if (i == pointer.Length || (path[i] != '0' && path[i] != '1')) return false;
        }
        String token = new();
        nuint position = 0;
        while (position < pointer.Length)
        {
            position++; // Consume '/', retaining empty and trailing segments.
            nuint end = position;
            while (end < pointer.Length && path[end] != '/') end++;
            token.Clear();
            token.Reserve(end - position);
            while (position < end)
            {
                char next = path[position++];
                if (next == '~')
                {
                    next = path[position++];
                    if (next == '0') next = '~'; else next = '/';
                }
                token.Append(next);
            }
            Span<readonly(char)> child = new();
            if (!SelectChild(selected, token.Data, token.Length, ref child)) return false;
            selected = child;
        }
        value = selected;
        return true;
    }

    private static bool SelectChild(Span<readonly(char)> parent,
        readonly(char)* token, nuint count, ref Span<readonly(char)> child)
    {
        readonly(char)* data = parent.Data!;
        JsonReader reader = new(data, parent.Length);
        reader.Read();
        bool objectValue = reader.Kind == JsonTokenKind::StartObject;
        if (!objectValue && reader.Kind != JsonTokenKind::StartArray) return false;
        nuint wanted = 0;
        if (!objectValue)
        {
            if (count == 0 || (count > 1 && token[0] == '0')) return false;
            for (nuint i = 0; i < count; i++)
            {
                if (token[i] < '0' || token[i] > '9') return false;
                nuint digit = (nuint)(token[i] - '0');
                if (wanted > (((nuint)0 - (nuint)1) - digit) / 10) return false;
                wanted = wanted * 10 + digit;
            }
        }
        bool found = false;
        Span<readonly(char)> candidate = new();
        nuint index = 0;
        while (reader.Read())
        {
            if (reader.Kind == JsonTokenKind::EndObject || reader.Kind == JsonTokenKind::EndArray) break;
            bool matches = false;
            if (objectValue)
            {
                matches = reader.ValueTextEquals(token, count);
                reader.Read();
            }
            else { matches = index == wanted; index++; }
            nuint start = reader.TokenOffset;
            reader.Skip();
            if (matches)
            {
                if (found) return false; // Includes escaped-equivalent duplicate names.
                found = true;
                candidate = new(data + start, reader.TokenOffset + reader.TokenLength - start);
            }
        }
        if (!found) return false;
        child = candidate;
        return true;
    }
}
