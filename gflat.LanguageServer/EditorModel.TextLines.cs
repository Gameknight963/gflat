namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private static string PreferredNewline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" :
        text.Contains('\r') && !text.Contains('\n') ? "\r" : "\n";

    private static int LineStart(string text, int offset) => text.AsSpan(0, offset).LastIndexOfAny('\r', '\n') + 1;

    private static int PreviousLineStart(string text, int start)
    {
        if (start == 0) return 0;
        int end = start - 1;
        if (text[end] == '\n' && end > 0 && text[end - 1] == '\r') end--;
        return LineStart(text, end);
    }
}
