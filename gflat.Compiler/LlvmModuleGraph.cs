using System.Text;

namespace gflat;

// Dependencies between generated LLVM functions, globals, and declarations.
// Inline user LLVM is kept as a whole and contributes explicit roots instead.
internal sealed class LlvmModuleGraph
{
    private sealed record Fragment(string Text, string? Name);
    private readonly List<Fragment> fragments = new();
    private readonly Dictionary<string, HashSet<string>> dependencies = new(StringComparer.Ordinal);
    private readonly HashSet<string> definitions = new(StringComparer.Ordinal);
    public HashSet<string> Roots { get; } = new(StringComparer.Ordinal);
    public bool Defines(string name) => definitions.Contains(name);
    public IEnumerable<string> References(string name) => dependencies.GetValueOrDefault(name) ?? [];

    public void Add(string text)
    {
        foreach (string record in Records(text))
        {
            string trimmed = record.TrimStart();
            bool function = trimmed.StartsWith("define ", StringComparison.Ordinal);
            bool declaration = trimmed.StartsWith("declare ", StringComparison.Ordinal);
            bool global = trimmed.StartsWith('@');
            string[] references = Symbols(record).Distinct(StringComparer.Ordinal).ToArray();
            string? name = function || declaration || global ? references.FirstOrDefault() : null;
            fragments.Add(new(record, name));
            if (name == null)
            {
                Roots.UnionWith(references);
                continue;
            }
            if (!dependencies.TryGetValue(name, out var edges)) dependencies[name] = edges = new(StringComparer.Ordinal);
            edges.UnionWith(references.Skip(1));
            if (declaration) continue;
            definitions.Add(name);
            int headerEnd = global ? trimmed.IndexOf('=') + 1 : 0;
            string header = trimmed[headerEnd..];
            if (function) header = header[..header.IndexOf('@')];
            string[] words = header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (global) words = words.TakeWhile(word => word is not "global" and not "constant" and not "alias" and not "ifunc").ToArray();
            if (!words.Contains("internal") && !words.Contains("private")) Roots.Add(name);
            if (name is "llvm.global_ctors" or "llvm.global_dtors" or "llvm.used" or "llvm.compiler.used") Roots.Add(name);
        }
    }

    public HashSet<string> Reachable(IEnumerable<string>? additionalRoots = null)
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(Roots.Concat(additionalRoots ?? []));
        while (pending.TryPop(out string? name))
        {
            if (!live.Add(name)) continue;
            foreach (string reference in References(name)) pending.Push(reference);
        }
        return live;
    }

    public string Render(HashSet<string> live)
    {
        var result = new StringBuilder();
        foreach (var fragment in fragments)
            if (fragment.Name == null || live.Contains(fragment.Name)) result.Append(fragment.Text);
        return result.ToString();
    }

    internal static IEnumerable<string> Symbols(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ';') { while (i < text.Length && text[i] != '\n') i++; }
            else if (text[i] == '"') SkipString(text, ref i);
            else if (text[i] == '@' && i + 1 < text.Length)
            {
                if (text[++i] == '"')
                {
                    var name = new StringBuilder();
                    for (i++; i < text.Length && text[i] != '"'; i++)
                    {
                        if (text[i] == '\\' && i + 2 < text.Length && char.IsAsciiHexDigit(text[i + 1]) && char.IsAsciiHexDigit(text[i + 2]))
                        {
                            name.Append((char)Convert.ToByte(text.Substring(i + 1, 2), 16));
                            i += 2;
                        }
                        else name.Append(text[i]);
                    }
                    yield return name.ToString();
                }
                else
                {
                    int start = i;
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '$' or '.' or '-')) i++;
                    if (i > start) yield return text[start..i];
                    i--;
                }
            }
        }
    }

    private static void SkipString(string text, ref int i)
    {
        for (i++; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == '"') return;
        }
    }

    private static IEnumerable<string> Records(string text)
    {
        int start = 0, depth = 0;
        bool function = false, body = false, symbol = false, started = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!started && !char.IsWhiteSpace(c) && c != ';')
            {
                function = text.AsSpan(i).StartsWith("define ");
                started = true;
            }
            if (c == '"') SkipString(text, ref i);
            else if (c == ';') { while (i < text.Length && text[i] != '\n') i++; i--; }
            else if (c == '@') symbol = true;
            else if (c is '(' or '[' or '{' or '<')
            {
                if (c == '{' && depth == 0 && symbol) body = true;
                depth++;
            }
            else if (c is ')' or ']' or '}' or '>') depth--;
            if (i < text.Length && text[i] == '\n' && depth == 0 && (!function || body))
            {
                yield return text[start..(i + 1)];
                start = i + 1;
                function = body = symbol = started = false;
            }
        }
        if (start < text.Length) yield return text[start..];
    }
}
