using System.Net;
using System.Text;

namespace gflat.LanguageServer;

public sealed record DocumentationText(string Kind, string Text, string? Target = null);
public sealed record DocumentationEntrySection(string Kind, string? Name, DocumentationText[] Content);
public sealed record DocumentationEntry(string Id, string Name, string Signature, Location Source,
    DocumentationText[] Summary, DocumentationEntrySection[] Sections);

public static class DocumentationManual
{
    public static string Html(IEnumerable<DocumentationEntry> entries)
    {
        var ordered = entries.OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Signature, StringComparer.Ordinal).ToArray();
        var ids = ordered.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        static string Encode(string text) => WebUtility.HtmlEncode(text);
        static string Anchor(string id) => "symbol-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..24];
        string Content(IEnumerable<DocumentationText> parts) => string.Concat(parts.Select(part =>
            part.Target != null && ids.Contains(part.Target) ? "<a href=\"#" + Anchor(part.Target) + "\"><code>" + Encode(part.Text) + "</code></a>" :
            part.Kind is "code" or "reference" ? "<code>" + Encode(part.Text) + "</code>" : Encode(part.Text)));
        var result = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>API documentation</title>");
        result.Append("<style>body{max-width:65rem;margin:2rem auto;padding:0 1rem;font:1rem/1.6 system-ui}pre{overflow:auto;padding:1rem;background:#f3f3f3}section{border-top:1px solid #ccc;margin-top:2rem}p,dd{white-space:pre-wrap}code{font-family:monospace}dt{font-weight:600}</style></head><body><h1>API documentation</h1><nav><ul>");
        foreach (var entry in ordered) result.Append("<li><a href=\"#").Append(Anchor(entry.Id)).Append("\">").Append(Encode(entry.Name)).Append("</a></li>");
        result.Append("</ul></nav>");
        foreach (var entry in ordered)
        {
            result.Append("<section id=\"").Append(Anchor(entry.Id)).Append("\"><h2>").Append(Encode(entry.Name)).Append("</h2><pre><code>")
                .Append(Encode(entry.Signature)).Append("</code></pre><p>").Append(Content(entry.Summary)).Append("</p>");
            foreach (var section in entry.Sections)
            {
                if (section.Kind == "code") result.Append("<pre><code>").Append(Encode(string.Concat(section.Content.Select(p => p.Text)))).Append("</code></pre>");
                else if (section.Kind == "details") result.Append("<p>").Append(Content(section.Content)).Append("</p>");
                else result.Append("<dl><dt>").Append(Encode(section.Kind switch { "param" => "Parameter", "tparam" => "Type parameter", "return" => "Returns", "throws" => "Throws", _ => section.Kind }))
                    .Append(section.Name == null ? "" : " " + Encode(section.Name)).Append("</dt><dd>").Append(Content(section.Content)).Append("</dd></dl>");
            }
            result.Append("</section>");
        }
        return result.Append("</body></html>").ToString();
    }
}
