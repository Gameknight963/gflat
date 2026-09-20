namespace gflat.CompileExceptions
{
    public class TypeCheckException : Exception
    {
        public int Line { get; }
        public SourceSpan Span { get; }
        public string? FilePath => Span.Source?.Path;
        public string Description { get; }
        public TypeCheckException(string message, int line) : this(message, SourceContext.ForLine(line)) { }
        public TypeCheckException(string message, SourceSpan span)
            : base(span.Source == null ? $"line {span.Line}: {message}" : $"{span.Source.Path}({span.Line},{span.Column}): {message}")
        { Line = span.Line; Span = span; Description = message; }
    }
}
