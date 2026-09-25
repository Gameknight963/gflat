using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace gflat.diagnostics
{
    public class DiagnosticBag : System.Collections.Generic.IEnumerable<Diagnostic>
    {
        private readonly List<Diagnostic> _items = new();

        public IReadOnlyList<Diagnostic> Items => _items;
        public int Count => _items.Count;

        public bool HasErrors => _items.Any(d => d.Severity == DiagnosticSeverity.Error);
        public bool HasWarnings => _items.Any(d => d.Severity == DiagnosticSeverity.Warning);
        public int ErrorCount => _items.Count(d => d.Severity == DiagnosticSeverity.Error);
        public int WarningCount => _items.Count(d => d.Severity == DiagnosticSeverity.Warning);

        public IEnumerator<Diagnostic> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Report(Diagnostic diagnostic)
        {
            _items.Add(diagnostic);
        }

        public void Report(DiagnosticDescriptor descriptor, SourceSpan span, params object[] args)
            => _items.Add(new Diagnostic(descriptor, span, args));

        public void Report(DiagnosticDescriptor descriptor, int line, int column = 0, string? filePath = null, params object[] args)
        {
            _items.Add(new Diagnostic(descriptor, line, column, filePath, args));
        }

        public void ReportWarning(string message, int line, int column = 0, string? filePath = null)
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("GF9998", "General Warning", message, DiagnosticSeverity.Warning, "General");
            _items.Add(new Diagnostic(descriptor, line, column, filePath));
        }

        public void ReportError(string message, int line, int column = 0, string? filePath = null)
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("GF9999", "General Error", message, DiagnosticSeverity.Error, "General");
            _items.Add(new Diagnostic(descriptor, line, column, filePath));
        }

        public void ClearSince(int mark)
        {
            if (mark >= 0 && mark < _items.Count)
            {
                _items.RemoveRange(mark, _items.Count - mark);
            }
        }

        public string FormatAll(bool useColor = false)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                }
                sb.Append(_items[i].ToString(useColor));
            }
            return sb.ToString();
        }
    }
}
