using System.Text;
using gflat.diagnostics;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private HashSet<string>? generatedMacroNames;

    private string ProtectGeneratedCode(string text)
    {
        if (text.Length == 0 || snapshot.MacroSymbols.Count == 0 && snapshot.Options.Defines.Count == 0) return text;
        var names = generatedMacroNames ??= snapshot.MacroSymbols.Where(symbol => symbol.Classification == "macro")
            .Select(symbol => symbol.Name).Concat(snapshot.Options.Defines.Keys).ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0) return text;
        var tokens = Lexer.Tokenize(new SourceFile("<generated-code>", text), new DiagnosticBag());
        StringBuilder? result = null;
        int cursor = 0, escapedDepth = 0;
        foreach (var token in tokens)
        {
            // Existing suppression groups already preserve these tokens.
            if (escapedDepth > 0)
            {
                if (token.Kind == TokenKind.OpenParen) escapedDepth++;
                else if (token.Kind == TokenKind.CloseParen) escapedDepth--;
                continue;
            }
            if (token.Kind == TokenKind.OpenParen && token.Start > 0 && text[token.Start - 1] == '@')
            { escapedDepth = 1; continue; }
            if (!names.Contains(token.Text) || token.Start > 0 && text[token.Start - 1] == '@' ||
                token.Kind != TokenKind.Identifier && Lexer.GetKeyword(token.Text) != token.Kind) continue;
            result ??= new();
            result.Append(text, cursor, token.Start - cursor);
            // Group suppression retains keyword kinds as well as identifiers.
            result.Append("@(").Append(text, token.Start, token.End - token.Start + 1).Append(')');
            cursor = token.End + 1;
        }
        return result?.Append(text, cursor, text.Length - cursor).ToString() ?? text;
    }
}
