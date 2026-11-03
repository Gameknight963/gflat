namespace gflat.Preprocessing;

public sealed partial class Preprocessor
{
    private IReadOnlyList<MacroToken> MacroDeclarationTokens(Macro macro, List<Piece> declaration)
    {
        var result = new List<MacroToken> { new("#define ", "preprocessor"), new(macro.Name, "macro", macro.Span) };
        if (macro.Parameters != null)
        {
            var commas = declaration.Skip(2).TakeWhile(p => p.Text != ")").Where(p => p.Text == ",").ToArray();
            result.Add(new("(", "punctuation", declaration[1].Span));
            for (int i = 0; i < macro.Parameters.Length; i++)
            {
                if (i != 0) result.Add(new(", ", "punctuation", commas[i - 1].Span));
                string name = macro.Parameters[i];
                result.Add(new(name, "identifier", declaration.Skip(1).First(p => p.Text == name).Span));
            }
            if (macro.Variadic)
            {
                if (macro.Parameters.Length != 0) result.Add(new(", ", "punctuation", commas[^1].Span));
                result.Add(new("...", "punctuation", declaration.First(p => p.Text == "...").Span));
            }
            result.Add(new(")", "punctuation", declaration.First(p => p.Text == ")").Span));
        }
        if (macro.Body.Count != 0)
        {
            result.Add(new(" ", "text"));
            result.AddRange(ClassifiedPieces(macro.Body, macro.Parameters, preserveJoins: true));
        }
        return result;
    }

    private List<MacroToken> ClassifiedPieces(List<Piece> pieces, string[]? parameters = null, bool preserveJoins = false)
    {
        var classifications = new string[pieces.Count];
        int rawEnd = -1;
        for (int i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            bool escapedIdentifier = i > 0 && pieces[i - 1].Text == "@" && Name(piece.Text);
            if (piece.Text == "@" && i + 1 < pieces.Count)
            {
                rawEnd = i + 1;
                if (pieces[i + 1].Text == "(")
                {
                    rawEnd = pieces.Count - 1;
                    int nesting = 0;
                    for (int n = i + 1; n < pieces.Count; n++)
                    {
                        if (pieces[n].Text == "(") nesting++;
                        else if (pieces[n].Text == ")" && --nesting == 0) { rawEnd = n; break; }
                    }
                }
            }
            classifications[i] = ClassifyMacroPiece(piece, parameters, i <= rawEnd, escapedIdentifier);
        }
        var ranges = new List<(int Start, int End, Piece Piece)>();
        var rendered = preserveJoins ? pieces.Select((piece, index) => piece with { JoinLeft = index > 0 && !piece.Space }).ToList() : pieces;
        string text = Render(rendered, (start, end, piece) => ranges.Add((start, end, piece)));
        var result = new List<MacroToken>();
        int previous = 0;
        for (int i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (range.Start > previous) result.Add(new(text[previous..range.Start], "text"));
            // Interpolation contains independently classified expressions in source.
            var span = range.Piece.Text.StartsWith("$\"", StringComparison.Ordinal) ? default : range.Piece.Span;
            result.Add(new(text[range.Start..range.End], classifications[i], span));
            previous = range.End;
        }
        return result;
    }

    private string ClassifyMacroPiece(Piece piece, string[]? parameters, bool raw, bool escapedIdentifier)
    {
        string text = piece.Text;
        if (piece.LiteralFragment || text.StartsWith('"') || text.StartsWith('\'') || text.StartsWith("$\"", StringComparison.Ordinal)) return "string";
        if (text.Length != 0 && (char.IsAsciiDigit(text[0]) || text.Length > 1 && text[0] == '.' && char.IsAsciiDigit(text[1]))) return "number";
        if (!Name(text)) return text is "(" or ")" or "[" or "]" or "{" or "}" or "," or ";" or "..." ? "punctuation" : "operator";
        if (piece.Verbatim || escapedIdentifier || parameters?.Contains(text) == true) return "identifier";
        if (!raw && (macros.ContainsKey(text) || text is "__LINE__" or "__FILE__")) return "macro";
        if (text == "this") return "modifier";
        var kind = Lexer.GetKeyword(text);
        if (kind == TokenKind.Identifier) return "identifier";
        return kind is TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.Foreach or TokenKind.In or
            TokenKind.While or TokenKind.Return or TokenKind.Break or TokenKind.Continue or TokenKind.Try or
            TokenKind.Catch or TokenKind.Throw or TokenKind.Defer ? "keyword" : "modifier";
    }

    private static List<MacroToken> LimitMacroTokens(List<MacroToken> tokens, int maximum, bool moreTokens)
    {
        var result = new List<MacroToken>();
        foreach (var token in tokens)
        {
            if (token.Text.Length > maximum)
            {
                if (maximum > 0) result.Add(token with { Text = token.Text[..maximum] });
                result.Add(new("…", "text"));
                return result;
            }
            result.Add(token);
            maximum -= token.Text.Length;
        }
        if (moreTokens) result.Add(new(" …", "text"));
        return result;
    }
}
