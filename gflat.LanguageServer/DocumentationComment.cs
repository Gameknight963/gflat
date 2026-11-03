using System.Text;
using System.Text.RegularExpressions;

namespace gflat.LanguageServer;

public sealed record DocumentationInline(string Kind, string Text, SourceSpan Span);
public sealed record DocumentationSection(string Kind, string? Name, SourceSpan NameSpan, DocumentationInline[] Content);
public sealed record DocumentationIssue(SourceSpan Span, string Message);
public sealed record DocumentationComment(DocumentationInline[] Summary, DocumentationSection[] Sections, DocumentationIssue[] Issues)
{
    public string SummaryText => string.Concat(Summary.Select(p => p.Text));
    public static string Text(IEnumerable<DocumentationInline> parts) => string.Concat(parts.Select(p => p.Text));

    public static DocumentationComment Parse(string text, Func<int, int, SourceSpan> span)
    {
        var issues = new List<DocumentationIssue>();
        var sections = new List<DocumentationSection>();
        var body = new StringBuilder();
        string kind = "brief";
        string? name = null;
        SourceSpan nameSpan = default;
        int bodyStart = 0;
        bool inCode = false;
        void Flush()
        {
            string value = body.ToString().TrimEnd('\n');
            if (value.Length > 0 || name != null) sections.Add(new(kind, name, nameSpan, Inline(value, bodyStart)));
            body.Clear();
        }
        DocumentationInline[] Inline(string value, int start)
        {
            if (kind == "code") return [new("code", value, span(start, value.Length))];
            var result = new List<DocumentationInline>();
            var matches = Regex.Matches(value, @"(?<!\S)[@\\](ref|c)(?:\s+|$)");
            int at = 0;
            foreach (Match match in matches)
            {
                if (match.Index < at) continue;
                if (match.Index > at) result.Add(new("text", value[at..match.Index], span(start + at, match.Index - at)));
                int first = match.Index + match.Length, end = first, nesting = 0;
                bool quoted = first < value.Length && value[first] == '"';
                if (quoted) first = ++end;
                while (end < value.Length)
                {
                    char c = value[end];
                    if (quoted && c == '"' || !quoted && nesting == 0 && char.IsWhiteSpace(c)) break;
                    if (c is '(' or '<' or '[') nesting++;
                    if (c is ')' or '>' or ']') { if (nesting == 0) break; nesting--; }
                    end++;
                }
                if (!quoted)
                    while (end > first && value[end - 1] is '.' or ',' or ';' or ':') end--;
                if (end == first)
                {
                    issues.Add(new(span(start + match.Index, match.Length), "Expected an argument after @" + match.Groups[1].Value));
                    at = first;
                    continue;
                }
                result.Add(new(match.Groups[1].Value == "ref" ? "reference" : "code", value[first..end], span(start + first, end - first)));
                at = quoted && end < value.Length && value[end] == '"' ? end + 1 : end;
            }
            if (at < value.Length) result.Add(new("text", value[at..], span(start + at, value.Length - at)));
            return result.ToArray();
        }
        int offset = 0;
        foreach (string line in text.Split('\n'))
        {
            var command = Regex.Match(line, @"^\s*[@\\]([A-Za-z]+)(?:\s+|$)");
            string tag = command.Success ? command.Groups[1].Value : "";
            if (inCode && tag != "endcode")
            {
                body.Append(line).Append('\n');
                offset += line.Length + 1;
                continue;
            }
            if (tag.Length > 0 && tag is not ("ref" or "c"))
            {
                Flush();
                string rest = line[command.Length..];
                int restStart = offset + command.Length;
                name = null; nameSpan = default;
                kind = tag switch { "return" or "returns" => "return", "throws" or "throw" or "exception" => "throws", _ => tag };
                if (tag == "endcode")
                {
                    if (!inCode) issues.Add(new(span(offset, line.Length), "@endcode has no matching @code"));
                    inCode = false; kind = "details";
                }
                else if (kind == "code") inCode = true;
                else if (kind is "param" or "tparam" or "throws")
                {
                    int end = rest.IndexOfAny([' ', '\t']);
                    if (end < 0) end = rest.Length;
                    name = rest[..end]; nameSpan = span(restStart, end);
                    if (name.Length == 0) issues.Add(new(span(offset, line.Length), "Expected a name after @" + tag));
                    restStart += end;
                    rest = rest[end..];
                    int leading = rest.Length - rest.TrimStart().Length;
                    restStart += leading; rest = rest.TrimStart();
                }
                else if (kind is not ("brief" or "details" or "return"))
                    issues.Add(new(span(offset, command.Length), "Unknown documentation command @" + tag));
                bodyStart = restStart;
                if (rest.Length > 0) body.Append(rest).Append('\n');
            }
            else if (string.IsNullOrWhiteSpace(line) && kind == "brief")
            {
                Flush(); kind = "details"; bodyStart = offset + line.Length + 1;
            }
            else
            {
                if (body.Length == 0) bodyStart = offset;
                body.Append(line).Append('\n');
            }
            offset += line.Length + 1;
        }
        Flush();
        if (inCode) issues.Add(new(span(Math.Max(0, text.Length - 1), Math.Min(1, text.Length)), "@code requires @endcode"));
        var summary = sections.Where(s => s.Kind == "brief").SelectMany(s => s.Content).ToArray();
        return new(summary, sections.Where(s => s.Kind != "brief").ToArray(), issues.ToArray());
    }
}
