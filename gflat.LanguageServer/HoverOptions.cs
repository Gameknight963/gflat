using System.Text.Json;

namespace gflat.LanguageServer;

public sealed record HoverOptions(bool ShowKnownExceptions = false, bool ShowTypeLayout = true)
{
    public HoverOptions Update(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return this;
        if (settings.TryGetProperty("gflat", out var language)) settings = language;
        if (settings.ValueKind != JsonValueKind.Object) return this;
        bool Read(string name, bool current) => settings.TryGetProperty(name, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : current;
        return new(Read("showKnownExceptions", ShowKnownExceptions), Read("showTypeLayout", ShowTypeLayout));
    }
}
