using gflat.ast;
using gflat.diagnostics;

namespace gflat;

// Session-local parser templates. Checking and lowering see only private copies.
// Preprocessed translation units are excluded by Compilation.
public sealed class SourceSyntaxCache
{
    private sealed record Entry(SourceFile Source, bool RecoverErrors, CompilationUnit Tree, Diagnostic[] Diagnostics,
        LinkedListNode<string> Recency)
    {
        public int CloneSize;
    }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recency = new();
    private readonly object gate = new();
    private const int Capacity = 1024;
    internal int ReusedTrees { get; private set; }

    internal CompilationUnit Parse(SourceFile source, List<Token> tokens, DiagnosticBag diagnostics, bool recoverErrors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry;
        lock (gate)
        {
            if (entries.TryGetValue(source.Path, out entry!) && entry.Source.Text == source.Text && entry.RecoverErrors == recoverErrors)
            {
                recency.Remove(entry.Recency);
                recency.AddLast(entry.Recency);
                ReusedTrees++;
            }
            else
            {
                var captured = new DiagnosticBag();
                var tree = Parser.Parse(tokens, captured, recoverErrors);
                tree.Span = source.Span(0, source.Text.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Remove(source.Path, out var old)) recency.Remove(old.Recency);
                var position = recency.AddLast(source.Path);
                entry = new(source, recoverErrors, tree, captured.Items.ToArray(), position);
                entries[source.Path] = entry;
                if (entries.Count > Capacity)
                {
                    entries.Remove(recency.First!.Value);
                    recency.RemoveFirst();
                }
            }
        }
        CompilationUnit result;
        try
        {
            var cloner = new SyntaxTreeCloner(cancellationToken, Volatile.Read(ref entry.CloneSize));
            result = cloner.Clone(entry.Tree);
            Volatile.Write(ref entry.CloneSize, cloner.ObjectCount);
        }
        catch (NotSupportedException)
        {
            // New syntax storage shapes must never result in shared mutable data.
            // Fall back to parsing until their copying rules are supported.
            lock (gate)
                if (entries.TryGetValue(source.Path, out var current) && ReferenceEquals(current, entry))
                { entries.Remove(source.Path); recency.Remove(entry.Recency); }
            return Parser.Parse(tokens, diagnostics, recoverErrors);
        }
        foreach (var diagnostic in entry.Diagnostics) diagnostics.Report(diagnostic);
        return result;
    }
}
