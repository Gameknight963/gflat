using System.Text.Json;
using System.Text.Json.Serialization;

namespace gflat.LanguageServer;

public sealed record DisplayPart(string Kind, string Text);
public sealed record Markup(string Kind, string Value);
public sealed record HoverResult(Markup Contents, TextRange Range,
    [property: JsonPropertyName("_vs_rawContent")] object? RawContent = null);

/// <summary>VS's existing LSP rich-content wire format. No VS assemblies or UI in the server.</summary>
public sealed class VisualStudioHover
{
    private readonly Dictionary<string, (Guid Guid, int Id)> icons = [];
    public VisualStudioHover(JsonElement catalog)
    {
        if (catalog.ValueKind != JsonValueKind.Object) throw new ArgumentException("hoverIcons must be an object");
        foreach (var icon in catalog.EnumerateObject())
        {
            if (icon.Value.TryGetProperty("guid", out var guid) && Guid.TryParse(guid.GetString(), out var parsed) &&
                icon.Value.TryGetProperty("id", out var id) && id.TryGetInt32(out int number) && number >= 0)
                icons[icon.Name] = (parsed, number);
        }
    }
    public object Render(DisplayPart[] signature, string[] details, string glyph)
    {
        var header = new List<object>();
        if (icons.TryGetValue(glyph, out var image) || icons.TryGetValue(glyph.Split('.')[0] + ".public", out image))
            header.Add(new Dictionary<string, object> {
                ["_vs_type"] = "ImageElement", ["AutomationName"] = glyph.Split('.')[0],
                ["ImageId"] = new Dictionary<string, object> { ["_vs_type"] = "ImageId", ["Guid"] = image.Guid, ["Id"] = image.Id }
            });
        header.Add(Text(signature));
        return Container(1, new[] { Container(0, header) }.Concat(details.Select(d => Text([new("text", d)]))).ToArray());
    }
    private static object Container(int style, IEnumerable<object> children) => new Dictionary<string, object>
    { ["_vs_type"] = "ContainerElement", ["Style"] = style, ["Elements"] = children.ToArray() };
    private static object Text(DisplayPart[] parts) => new Dictionary<string, object>
    {
        ["_vs_type"] = "ClassifiedTextElement",
        ["Runs"] = parts.Select(p => new Dictionary<string, object> {
            ["_vs_type"] = "ClassifiedTextRun", ["ClassificationTypeName"] = Classification(p.Kind), ["Text"] = p.Text, ["Style"] = 0
        }).ToArray()
    };
    private static string Classification(string kind) => kind switch
    {
        "class" => "class name", "struct" => "struct name", "interface" => "interface name", "enum" => "enum name",
        "namespace" => "namespace name", "method" or "function" => "method name", "parameter" => "parameter name",
        "variable" => "local name", "property" => "property name", "enumMember" => "enum member name", "number" => "number",
        _ => kind
    };
}
