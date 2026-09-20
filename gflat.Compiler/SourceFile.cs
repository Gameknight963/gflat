namespace gflat;

public readonly record struct SourceId(string Path);

/// <summary>An immutable source snapshot; text need not have been saved to disk.</summary>
public sealed class SourceFile
{
    public SourceId Id { get; }
    public string Path => Id.Path;
    public string Text { get; }
    private readonly int[] lineStarts;

    public SourceFile(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        Id = new(path);
        Text = text;
        lineStarts = new[] { 0 }.Concat(text.Select((c, i) => (c, i)).Where(x => x.c == '\n').Select(x => x.i + 1)).ToArray();
    }

    public SourceSpan Span(int start, int length = 0)
    {
        start = Math.Clamp(start, 0, Text.Length);
        int index = Array.BinarySearch(lineStarts, start);
        if (index < 0) index = ~index - 1;
        return new(start, Math.Clamp(length, 0, Text.Length - start), index + 1, start - lineStarts[index] + 1, this);
    }

    public SourceSpan LineSpan(int line)
    {
        int start = lineStarts[Math.Clamp(line - 1, 0, lineStarts.Length - 1)];
        int end = Text.IndexOf('\n', start);
        if (end < 0) end = Text.Length;
        while (start < end && char.IsWhiteSpace(Text[start])) start++;
        return Span(start, end - start);
    }
}

// Scoped diagnostic context only. Each asynchronous compilation has its own context.
internal static class SourceContext
{
    private static readonly AsyncLocal<SourceSpan?> current = new();
    public static SourceSpan? Current => current.Value;
    public static SourceSpan ForLine(int line) => current.Value is { Source: not null } span
        ? (span.Line == line || line == 0 ? span : span.Source.LineSpan(line))
        : new(0, 0, line, 0);
    public static IDisposable Enter(SourceSpan span)
    {
        var previous = current.Value;
        if (span.Source != null) current.Value = span;
        return new Scope(() => current.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}
