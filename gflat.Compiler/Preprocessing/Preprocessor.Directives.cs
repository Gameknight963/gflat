using gflat.diagnostics;

namespace gflat.Preprocessing;

public sealed partial class Preprocessor
{
    private sealed record Conditional(bool Parent, bool Active, bool Taken, bool Else, SourceSpan Start);

    private List<Piece> Read(SourceFile source, int depth)
    {
        cancellation.ThrowIfCancellationRequested();
        string key = Key(source.Path);
        dependencies.Add(key);
        if (once.Contains(key)) return [];
        if (depth > 128) throw Error("Include nesting limit exceeded. Check include guards", source.Span(0));
        files[key] = source;
        var input = Scan(source);
        var output = new List<Piece>();
        var pending = new List<Piece>();
        var conditions = new Stack<Conditional>();
        bool Active() => conditions.Count == 0 || conditions.Peek().Active;
        void Flush() { output.AddRange(Expand(pending)); pending.Clear(); }
        for (int i = 0; i < input.Count;)
        {
            int end = i;
            while (end < input.Count && input[end].Text != "\n") end++;
            var line = input.GetRange(i, end - i);
            i = Math.Min(input.Count, end + 1);
            if (line.Count == 0) continue;
            if (line[0].Text != "#")
            {
                if (Active()) pending.AddRange(line);
                continue;
            }
            Flush();
            if (line.Count == 1) continue;
            string directive = line[1].Text;
            var arguments = line.Skip(2).ToList();
            SourceSpan location = line[1].Span;
            symbols.Add(new(directive, source.Span(line[0].Span.Start,
                location.Start + location.Length - line[0].Span.Start), false, default,
                "#" + directive, Classification: "preprocessor"));
            if (directive is "if" or "ifdef" or "ifndef")
            {
                bool parent = Active();
                bool test = parent && (directive == "if" ? Evaluate(arguments, location) : DefinedName(arguments, location));
                if (parent && directive == "ifndef") test = !test;
                conditions.Push(new(parent, test, test, false, location));
                continue;
            }
            if (directive is "elif" or "elifdef" or "elifndef" or "else" or "endif")
            {
                if (conditions.Count == 0) throw Error($"#{directive} has no matching #if", location);
                var state = conditions.Pop();
                if (directive == "endif") { NoArguments(arguments, location); continue; }
                if (state.Else) throw Error($"#{directive} follows #else", location);
                bool selected;
                if (directive == "else") { NoArguments(arguments, location); selected = state.Parent && !state.Taken; }
                else selected = state.Parent && !state.Taken && (directive == "elif" ? Evaluate(arguments, location) :
                    directive == "elifdef" ? DefinedName(arguments, location) : !DefinedName(arguments, location));
                conditions.Push(state with { Active = selected, Taken = state.Taken || selected, Else = directive == "else" });
                continue;
            }
            if (!Active()) continue;
            switch (directive)
            {
                case "define": Define(arguments, location); break;
                case "undef":
                    RequireName(arguments, location);
                    Reference(arguments[0]);
                    if (arguments[0].Text is "__FILE__" or "__LINE__") throw Error("Cannot undefine a predefined macro", location);
                    macros.Remove(arguments[0].Text); break;
                case "include":
                    var header = arguments.Count > 0 && (arguments[0].Text.StartsWith('"') || arguments[0].Text == "<")
                        ? arguments : Expand(arguments);
                    bool quoted = header.Count == 1 && header[0].Text.StartsWith('"') && header[0].Text.EndsWith('"');
                    string name;
                    if (quoted) name = header[0].Text[1..^1];
                    else if (header.Count >= 3 && header[0].Text == "<" && header[^1].Text == ">")
                        name = string.Concat(header.Skip(1).Take(header.Count - 2).Select((piece, n) => (n > 0 && piece.Space ? " " : "") + piece.Text));
                    else throw Error("Expected a quoted or angle-bracket include name", location);
                    output.AddRange(Read(Include(source, name, quoted, location), depth + 1));
                    break;
                case "pragma":
                    if (arguments.Count == 1 && arguments[0].Text == "once") once.Add(key);
                    else diagnostics.Report(new DiagnosticDescriptor("GFPP0001", "Unknown pragma", "Ignored pragma: {0}", DiagnosticSeverity.Warning, "Preprocessor"), location, string.Join(" ", arguments.Select(p => p.Text)));
                    break;
                case "error": throw Error("#error " + string.Join(" ", arguments.Select(p => p.Text)), location);
                case "warning": diagnostics.Report(new DiagnosticDescriptor("GFPP0002", "Preprocessor warning", "{0}", DiagnosticSeverity.Warning, "Preprocessor"), location, string.Join(" ", arguments.Select(p => p.Text))); break;
                case "no_default_allocator": throw Error("#no_default_allocator is obsolete; use replace on Memory::Allocate and Memory::Free", location);
                default: throw Error($"Unknown directive '#{directive}'", location);
            }
        }
        Flush();
        if (conditions.Count != 0) throw Error("Unterminated conditional directive", conditions.Peek().Start);
        return output;
    }

    private SourceFile Include(SourceFile including, string name, bool quoted, SourceSpan location)
    {
        if (name.Length == 0) throw Error("Include name is empty", location);
        if (name.IndexOfAny(['\0', '\r', '\n']) >= 0) throw Error("Invalid character in include name", location);
        var directories = new List<string>();
        if (quoted) directories.Add(including.Path.StartsWith('<') ? Environment.CurrentDirectory : Path.GetDirectoryName(Key(including.Path))!);
        directories.AddRange(options.IncludeDirectories);
        foreach (string directory in directories)
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(directory, name));
                dependencies.Add(path);
                if (files.TryGetValue(path, out var cached)) return cached;
                if (options.IncludeLoader?.Invoke(path) is { } snapshot) return snapshot;
                if (File.Exists(path)) return new SourceFile(path, File.ReadAllText(path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { throw Error($"Cannot read include '{name}': {error.Message}", location); }
        }
        throw Error($"Include file '{name}' was not found", location);
    }

    private void Define(List<Piece> input, SourceSpan location)
    {
        if (input.Count == 0 || !Name(input[0].Text)) throw Error("Expected a macro name after #define", location);
        var name = input[0];
        if (name.Text is "__FILE__" or "__LINE__" or "__VA_ARGS__" or "__VA_OPT__" or "defined") throw Error($"Reserved macro name '{name.Text}'", name.Span);
        string[]? parameters = null;
        bool variadic = false;
        int bodyStart = 1;
        if (input.Count > 1 && input[1].Text == "(" && !input[1].Space)
        {
            int closing = Closing(input, 1);
            var parameterTokens = input.GetRange(2, closing - 2);
            var names = new List<string>();
            for (int i = 0; i < parameterTokens.Count; i += 2)
            {
                var parameter = parameterTokens[i];
                if (parameter.Text == "..." && i == parameterTokens.Count - 1) { variadic = true; break; }
                if (!Name(parameter.Text) || parameter.Text is "__VA_ARGS__" or "__VA_OPT__" || names.Contains(parameter.Text))
                    throw Error("Invalid or duplicate macro parameter", parameter.Span);
                names.Add(parameter.Text);
                if (i + 1 < parameterTokens.Count && (parameterTokens[i + 1].Text != "," || i + 2 == parameterTokens.Count))
                    throw Error("Expected another macro parameter after ','", parameterTokens[i + 1].Span);
            }
            parameters = names.ToArray(); bodyStart = closing + 1;
        }
        var body = input.Skip(bodyStart).ToList();
        if (body.Count != 0)
        {
            body[0] = body[0] with { Space = false };
            if (body[0].Text == "##" || body[^1].Text == "##") throw Error("## cannot begin or end a replacement list", location);
        }
        for (int i = 0; i < body.Count; i++)
        {
            if (body[i].Text == "#" && parameters != null &&
                (i + 1 == body.Count || !parameters.Contains(body[i + 1].Text) && !(variadic && body[i + 1].Text == "__VA_ARGS__")))
                throw Error("# must precede a macro parameter", body[i].Span);
            if (body[i].Text is "__VA_ARGS__" or "__VA_OPT__" && !variadic)
                throw Error("Variadic replacement requires a variadic macro", body[i].Span);
            if (body[i].Text == "__VA_OPT__")
            {
                int end = Closing(body, i + 1);
                if (body.Skip(i + 2).Take(end - i - 2).Any(p => p.Text == "__VA_OPT__")) throw Error("Nested __VA_OPT__ is not allowed", body[i].Span);
            }
        }
        var macro = new Macro(name.Text, parameters, variadic, body, name.Span);
        if (macros.TryGetValue(name.Text, out var previous) &&
            (previous.Variadic != variadic || (previous.Parameters == null) != (parameters == null) ||
             !(previous.Parameters ?? []).SequenceEqual(parameters ?? []) ||
             !previous.Body.Select(p => (p.Text, p.Space)).SequenceEqual(body.Select(p => (p.Text, p.Space)))))
            throw Error($"Conflicting redefinition of macro '{name.Text}'. Use #undef before replacing it", name.Span);
        macros[name.Text] = macro;
        symbols.Add(Symbol(macro, name.Span, true));
        for (int n = 0; n < body.Count; n++)
        {
            if (body[n].Text == "@" && n + 1 < body.Count)
            {
                n = body[n + 1].Text == "(" ? Closing(body, n + 1) : n + 1;
                continue;
            }
            if (!(parameters ?? []).Contains(body[n].Text)) Reference(body[n]);
        }
    }

    private bool DefinedName(List<Piece> input, SourceSpan span)
    {
        RequireName(input, span);
        Reference(input[0]);
        return macros.ContainsKey(input[0].Text) || input[0].Text is "__FILE__" or "__LINE__";
    }
    private static void RequireName(List<Piece> input, SourceSpan span)
    { if (input.Count != 1 || !Name(input[0].Text)) throw Error("Expected one macro name", span); }
    private static void NoArguments(List<Piece> input, SourceSpan span)
    { if (input.Count != 0) throw Error("Unexpected tokens after directive", span); }
}
