namespace gflat.diagnostics
{
    public class DiagnosticDescriptor
    {
        public string Id { get; }
        public string Title { get; }
        public string MessageFormat { get; }
        public DiagnosticSeverity DefaultSeverity { get; }
        public string Category { get; }

        public DiagnosticDescriptor(string id, string title, string messageFormat, DiagnosticSeverity severity, string category)
        {
            Id = id;
            Title = title;
            MessageFormat = messageFormat;
            DefaultSeverity = severity;
            Category = category;
        }
    }
}
