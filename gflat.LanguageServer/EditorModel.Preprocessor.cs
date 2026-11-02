using gflat.Preprocessing;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private MacroSymbol? MacroAt(string path, int offset) => snapshot.MacroSymbols.FirstOrDefault(symbol =>
        symbol.Span.Source != null && Workspace.Paths.Equals(symbol.Span.Source.Path, path) &&
        offset >= symbol.Span.Start && offset < symbol.Span.Start + symbol.Span.Length);

    private object? MacroHover(string path, int offset) => MacroAt(path, offset) is { } macro
        ? new HoverResult(new("markdown", "```cpp\n" + macro.Signature + "\n```"), Workspace.ToRange(macro.Span), null) : null;
}
