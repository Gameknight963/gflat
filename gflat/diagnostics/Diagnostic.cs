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

        public Diagnostic(DiagnosticDescriptor descriptor, int line, int column = 0, string? filePath = null, params object[] messageArgs)
        {
            Descriptor = descriptor;
            Severity = descriptor.DefaultSeverity;
            Line = line;
            Column = column;
            FilePath = filePath;
            Message = messageArgs != null && messageArgs.Length > 0
                ? string.Format(descriptor.MessageFormat, messageArgs)
                : descriptor.MessageFormat;
        }

        public Diagnostic(DiagnosticDescriptor descriptor, DiagnosticSeverity overrideSeverity, int line, int column = 0, string? filePath = null, params object[] messageArgs)
        {
            Descriptor = descriptor;
            Severity = overrideSeverity;
            Line = line;
            Column = column;
            FilePath = filePath;
            Message = messageArgs != null && messageArgs.Length > 0
                ? string.Format(descriptor.MessageFormat, messageArgs)
                : descriptor.MessageFormat;
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
                return $"{loc}: {severityStr} {Descriptor.Id}: {Message}";
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
