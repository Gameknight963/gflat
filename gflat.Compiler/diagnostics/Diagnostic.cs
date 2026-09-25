namespace gflat.diagnostics
{
    public class Diagnostic
    {
        public DiagnosticDescriptor Descriptor { get; }
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }
        public string? FilePath { get; }
        public SourceSpan Span { get; }
        public List<SourceSpan> RelatedLocations { get; } = new();

        public Diagnostic(DiagnosticDescriptor descriptor, SourceSpan span, params object[] messageArgs)
            : this(descriptor, descriptor.DefaultSeverity, span, messageArgs) { }

        public Diagnostic(DiagnosticDescriptor descriptor, DiagnosticSeverity severity, SourceSpan span, params object[] messageArgs)
        {
            Descriptor = descriptor;
            Severity = severity;
            Span = span;
            Line = span.Line;
            Column = span.Column;
            FilePath = span.Source?.Path;
            Message = messageArgs.Length > 0 ? string.Format(descriptor.MessageFormat, messageArgs) : descriptor.MessageFormat;
        }

        public Diagnostic(DiagnosticDescriptor descriptor, int line, int column = 0, string? filePath = null, params object[] messageArgs)
            : this(descriptor, descriptor.DefaultSeverity, LegacySpan(line, column), messageArgs)
        { FilePath = filePath ?? Span.Source?.Path; }

        public Diagnostic(DiagnosticDescriptor descriptor, DiagnosticSeverity overrideSeverity, int line, int column = 0, string? filePath = null, params object[] messageArgs)
            : this(descriptor, overrideSeverity, LegacySpan(line, column), messageArgs)
        { FilePath = filePath ?? Span.Source?.Path; }

        private static SourceSpan LegacySpan(int line, int column)
        {
            var span = SourceContext.ForLine(line);
            if (column <= 0 || column == span.Column) return span;
            return span.Source?.Span(span.Start - span.Column + column) ?? span with { Column = column };
        }

        public override string ToString()
        {
            return ToString(useColor: false);
        }

        public string ToString(bool useColor)
        {
            string loc = FilePath != null
                ? (Column > 0 ? $"{FilePath}({Line},{Column})" : $"{FilePath}({Line})")
                : (Column > 0 ? $"line {Line}:{Column}" : $"line {Line}");

            string severityStr = Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                _ => "diagnostic"
            };

            if (!useColor)
            {
                return $"{loc}: {severityStr} {Descriptor.Id}: {Message}" + string.Concat(RelatedLocations.Select(span => $"{Environment.NewLine}{span.Source?.Path ?? "<input>"}({span.Line},{span.Column}): note: previous declaration"));
            }

            string severityColor = Severity switch
            {
                DiagnosticSeverity.Error => "\u001b[31;1m",
                DiagnosticSeverity.Warning => "\u001b[33;1m",
                DiagnosticSeverity.Info => "\u001b[36;1m",
                _ => "\u001b[1m"
            };

            return $"\u001b[1m{loc}:\u001b[0m {severityColor}{severityStr}\u001b[0m \u001b[1m{Descriptor.Id}:\u001b[0m {Message}";
        }
    }
}
