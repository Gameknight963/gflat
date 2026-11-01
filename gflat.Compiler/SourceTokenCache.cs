using gflat.diagnostics;

namespace gflat;

// Editor sessions can share lexical snapshots. Parsing always creates a new AST.
public sealed class SourceTokenCache
{
    private sealed record Entry(SourceFile Source, List<Token> Tokens, Diagnostic[] Diagnostics);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private const int Capacity = 1024;

    internal List<Token> Tokenize(SourceFile source, DiagnosticBag? diagnostics)
    {
        Entry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(source.Path, out entry!) || entry.Source.Text != source.Text)
            {
                var captured = new DiagnosticBag();
                var tokens = Lexer.Tokenize(source, captured);
                entry = new(source, tokens, captured.Items.ToArray());
                if (entries.Count >= Capacity && !entries.ContainsKey(source.Path)) entries.Remove(entries.Keys.First());
                entries[source.Path] = entry;
            }
        }
        // Keep throwing lexers' behavior when recovery was not requested.
        if (diagnostics == null && entry.Diagnostics.Length != 0) return Lexer.Tokenize(source);
        if (diagnostics != null)
            foreach (var diagnostic in entry.Diagnostics) diagnostics.Report(diagnostic);
        return entry.Tokens;
    }
}
