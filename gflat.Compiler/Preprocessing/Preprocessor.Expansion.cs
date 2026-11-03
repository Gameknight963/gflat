namespace gflat.Preprocessing;

public sealed partial class Preprocessor
{
    private MacroSymbol Symbol(Macro macro, SourceSpan span, bool definition) => new(macro.Name, span, definition, macro.Span,
        string.Concat(macro.DeclarationTokens!.Select(t => t.Text)), DeclarationTokens: macro.DeclarationTokens);

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

    private void Reference(Piece token)
    {
        if (token.Generated) return;
        if (macros.TryGetValue(token.Text, out var macro))
            symbols.Add(Symbol(macro, token.Span, false));
        else if (token.Text is "__LINE__" or "__FILE__")
            symbols.Add(new(token.Text, token.Span, false, default, token.Text, DeclarationTokens: [new(token.Text, "macro")]));
    }

    private List<Piece> Expand(IEnumerable<Piece> input, int depth = 0)
    {
        var work = input.Where(p => p.Text != "\n").ToList();
        if (depth > 128) throw Error("Macro argument expansion is too deeply nested", work.FirstOrDefault()?.Span ?? default);
        var output = new List<Piece>();
        var previewIds = new HashSet<int>();
        for (int i = 0; i < work.Count;)
        {
            cancellation.ThrowIfCancellationRequested();
            var token = work[i];
            if (token.LiteralFragment) { output.Add(token); i++; continue; }
            if (token.Text.StartsWith("$\""))
            {
                output.AddRange(Interpolation(token, hole => Expand(hole, depth + 1)));
                i++; continue;
            }
            if (token.Text == "@")
            {
                if (i + 1 >= work.Count || work[i + 1].Space) throw Error("Expected an identifier or '(' immediately after '@'", token.Span);
                if (work[i + 1].Text == "(")
                {
                    int end = Closing(work, i + 1);
                    output.AddRange(work.GetRange(i + 2, end - i - 2).Select((piece, index) =>
                        piece with { JoinLeft = index > 0 && !piece.Space }));
                    i = end + 1;
                }
                else
                {
                    if (!Name(work[i + 1].Text)) throw Error("Expected an identifier after '@'", token.Span);
                    output.Add(work[i + 1] with { Verbatim = true });
                    i += 2;
                }
                continue;
            }
            if (token.Hidden?.Contains(token.Text) == true || !Name(token.Text)) { output.Add(token); i++; continue; }
            if (token.Text is "__LINE__" or "__FILE__")
            {
                string value = token.Text == "__LINE__" ? token.Span.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) : Quote(token.Span.Source?.Path ?? "<input>");
                if (!token.Generated) symbols.Add(new(token.Text, token.Span, false, default, token.Text, value,
                    DeclarationTokens: [new(token.Text, "macro")],
                    ExpansionTokens: [new(value, token.Text == "__LINE__" ? "number" : "string")], Invocation: token.Span, FullExpansion: value));
                output.Add(token with { Text = value, Generated = true, Verbatim = false });
                i++; continue;
            }
            if (!macros.TryGetValue(token.Text, out var macro)) { output.Add(token); i++; continue; }
            int consumed = 1;
            var arguments = new Dictionary<string, List<Piece>>(StringComparer.Ordinal);
            if (macro.Parameters != null)
            {
                if (i + 1 >= work.Count || work[i + 1].Text != "(") { Reference(token); output.Add(token); i++; continue; }
                int end = Closing(work, i + 1);
                var args = Arguments(work.GetRange(i + 2, end - i - 2));
                if (args.Count == 1 && args[0].Count == 0 && macro.Parameters.Length == 0 && !macro.Variadic) args.Clear();
                if (args.Count < macro.Parameters.Length || !macro.Variadic && args.Count != macro.Parameters.Length)
                    throw Error($"Macro '{macro.Name}' expects {macro.Parameters.Length}{(macro.Variadic ? " or more" : "")} arguments, received {args.Count}", token.Span);
                for (int n = 0; n < macro.Parameters.Length; n++) arguments[macro.Parameters[n]] = args[n];
                if (macro.Variadic)
                {
                    var rest = new List<Piece>();
                    for (int n = macro.Parameters.Length; n < args.Count; n++)
                    {
                        if (n != macro.Parameters.Length) rest.Add(new(",", token.Span));
                        rest.AddRange(args[n]);
                    }
                    arguments["__VA_ARGS__"] = rest;
                }
                consumed = end - i + 1;
            }
            if (++expansions > 100000 || work.Count + output.Count > 1000000)
                throw Error("Macro expansion limit exceeded", token.Span);
            int occurrence = -1;
            if (!token.Generated)
            {
                occurrence = symbols.Count;
                previewIds.Add(occurrence);
                var last = work[i + consumed - 1].Span;
                var invocation = token.Span.Source != null && last.Source == token.Span.Source && last.Start >= token.Span.Start
                    ? token.Span.Source.Span(token.Span.Start, last.Start + last.Length - token.Span.Start) : token.Span;
                symbols.Add(Symbol(macro, token.Span, false) with { Expansion = "", Invocation = invocation, FullExpansion = "" });
            }
            var expanded = new Dictionary<string, List<Piece>>(StringComparer.Ordinal);
            List<Piece> Argument(string name)
            {
                if (!expanded.TryGetValue(name, out var value))
                {
                    int before = symbols.Count;
                    expanded[name] = value = Expand(arguments[name], depth + 1);
                    var argumentSteps = symbols.Skip(before).SelectMany(symbol => symbol.Steps ?? []).Take(64).ToArray();
                    foreach (int id in (token.Occurrences ?? []).Concat(occurrence >= 0 ? [occurrence] : Array.Empty<int>()).Distinct())
                        symbols[id] = symbols[id] with { Steps = (symbols[id].Steps ?? []).Concat(argumentSteps).Take(64).ToArray() };
                }
                return value;
            }
            List<Piece> Substitute(List<Piece> body)
            {
                var result = new List<Piece>();
                for (int n = 0; n < body.Count; n++)
                {
                    var item = body[n];
                    if (!item.LiteralFragment && item.Text.StartsWith("$\""))
                    {
                        result.AddRange(Interpolation(item with { Span = token.Span, Generated = true }, Substitute));
                        continue;
                    }
                    if (item.Text == "__VA_OPT__")
                    {
                        int end = Closing(body, n + 1);
                        if (Argument("__VA_ARGS__").Count != 0) result.AddRange(Substitute(body.GetRange(n + 2, end - n - 2)));
                        else result.Add(new("", token.Span, Generated: true));
                        n = end; continue;
                    }
                    if (item.Text == "#" && n + 1 < body.Count && arguments.ContainsKey(body[n + 1].Text))
                    {
                        var value = Argument(body[++n].Text);
                        string spelling = string.Concat(value.Select((p, index) => (index > 0 && p.Space ? " " : "") + p.Text));
                        result.Add(new(Quote(spelling), token.Span, item.Space, Generated: true));
                    }
                    else if (arguments.ContainsKey(item.Text))
                    {
                        var value = Argument(item.Text);
                        if (value.Count == 0) result.Add(new("", token.Span, item.Space, Generated: true));
                        else
                        {
                            result.Add(value[0] with { Space = item.Space });
                            result.AddRange(value.Skip(1));
                        }
                    }
                    else result.Add(item with { Span = token.Span, Generated = true });
                }
                return result;
            }
            var replacement = Substitute(macro.Body);
            for (int n = 0; n < replacement.Count; n++)
            {
                if (replacement[n].Text != "##") continue;
                if (n == 0 || n + 1 == replacement.Count || replacement[n - 1].Text == "##" || replacement[n + 1].Text == "##")
                    throw Error("Token paste requires two operands", token.Span);
                var left = replacement[n - 1]; var right = replacement[n + 1];
                string joined = left.Text + right.Text;
                if (left.Text.Length != 0 && right.Text.Length != 0)
                {
                    List<Piece> pasted;
                    try { pasted = Scan(new SourceFile("<paste>", joined)); }
                    catch (gflat.CompileExceptions.TypeCheckException) { throw Error($"Token paste '{joined}' does not form one preprocessing token", token.Span); }
                    if (pasted.Count != 1 || pasted[0].Text != joined)
                        throw Error($"Token paste '{joined}' does not form one preprocessing token", token.Span);
                }
                HashSet<string>? hidden = left.Hidden == null || right.Hidden == null ? null : new(left.Hidden.Intersect(right.Hidden));
                replacement.RemoveRange(n - 1, 3);
                replacement.Insert(n - 1, new(joined, token.Span, left.Space, Hidden: hidden, Generated: true));
                n -= 1;
            }
            var suppressed = new HashSet<string>(token.Hidden ?? [], StringComparer.Ordinal) { macro.Name };
            replacement = replacement.Where(p => p.Text.Length != 0).Select(p => p with
            {
                Hidden = new HashSet<string>((p.Hidden ?? []).Concat(suppressed), StringComparer.Ordinal),
                Occurrences = (p.Occurrences ?? []).Concat(token.Occurrences ?? [])
                    .Concat(occurrence >= 0 ? [occurrence] : Array.Empty<int>()).Distinct().ToArray()
            }).ToList();
            if (replacement.Count != 0) replacement[0] = replacement[0] with { Space = token.Space };
            foreach (int id in (token.Occurrences ?? []).Concat(occurrence >= 0 ? [occurrence] : Array.Empty<int>()).Distinct())
            {
                if ((symbols[id].Steps?.Count ?? 0) >= 64) continue;
                static string Preview(string text) => text.Length <= 2048 ? text : text[..2048] + "...";
                var step = new MacroExpansionStep(macro.Name, Preview(Render(work.GetRange(i, consumed))), Preview(Render(replacement)), macro.Span);
                symbols[id] = symbols[id] with { Steps = (symbols[id].Steps ?? []).Append(step).ToArray() };
            }
            work.RemoveRange(i, consumed);
            work.InsertRange(i, replacement);
        }
        // Capture the tokens produced by this invocation during the actual expansion.
        // Re-expanding for a tooltip would use the wrong definitions after #undef.
        var previews = new Dictionary<int, List<Piece>>();
        foreach (var piece in output)
            foreach (int id in piece.Occurrences ?? [])
            {
                if (!previewIds.Contains(id)) continue;
                if (!previews.TryGetValue(id, out var preview)) previews[id] = preview = [];
                if (preview.Count < 512) preview.Add(piece);
            }
        foreach (var (id, preview) in previews)
        {
            var tokens = LimitMacroTokens(ClassifiedPieces(preview), 2048, preview.Count == 512);
            symbols[id] = symbols[id] with { Expansion = string.Concat(tokens.Select(t => t.Text)), ExpansionTokens = tokens,
                FullExpansion = preview.Count < 512 ? Render(preview) : null };
        }
        return output;
    }

    private List<Piece> Interpolation(Piece token, Func<List<Piece>, List<Piece>> transform)
    {
        var output = new List<Piece>();
        var delimiters = Lexer.ReadInterpolatedString(token.Text, 0, out _, token.Span.Line, tokenizeExpressions: false);
        bool exact = !token.Generated && token.Text.Length == token.Span.Length && token.Span.Source != null;
        SourceSpan Origin(int start, int length) => exact ? token.Span.Source!.Span(token.Span.Start + start, length) : token.Span;
        void Fragment(int start, int length, bool join)
            => output.Add(token with { Text = token.Text.Substring(start, length), Span = Origin(start, length),
                Generated = !exact, JoinLeft = join, Space = join ? false : token.Space, LiteralFragment = true });
        int start = 0;
        for (int n = 0; n < delimiters.Count; n++)
        {
            if (delimiters[n].Kind != TokenKind.InterpolatedStringExprStart) continue;
            int first = delimiters[n].End + 1, last = delimiters[++n].Start;
            Fragment(start, first - start, start != 0);
            var hole = Scan(new SourceFile("<interpolation>", token.Text[first..last]));
            hole = hole.Select(piece => piece with { Span = Origin(first + piece.Span.Start, piece.Span.Length), Generated = !exact }).ToList();
            var value = transform(hole);
            if (value.Count != 0) value[0] = value[0] with { JoinLeft = true };
            output.AddRange(value);
            start = last;
        }
        Fragment(start, token.Text.Length - start, start != 0);
        return output;
    }

    private static int Closing(List<Piece> pieces, int opening)
    {
        if (opening >= pieces.Count || pieces[opening].Text != "(") throw Error("Expected '(' ", pieces[Math.Min(opening, pieces.Count - 1)].Span);
        int depth = 0;
        for (int n = opening; n < pieces.Count; n++)
        {
            if (pieces[n].Text == "(") depth++;
            else if (pieces[n].Text == ")" && --depth == 0) return n;
        }
        throw Error("Unterminated parenthesized preprocessing tokens", pieces[opening].Span);
    }

    private static List<List<Piece>> Arguments(List<Piece> pieces)
    {
        var result = new List<List<Piece>> { new() };
        int depth = 0;
        foreach (var piece in pieces)
        {
            if (piece.Text == "(" ) depth++;
            if (piece.Text == ")") depth--;
            if (piece.Text == "," && depth == 0) result.Add(new());
            else result[^1].Add(piece);
        }
        return result;
    }
}
