namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private static int LineStart(string text, int offset) => offset == 0 ? 0 :
        Math.Max(text.LastIndexOf('\r', offset - 1), text.LastIndexOf('\n', offset - 1)) + 1;

    private static int PreviousLineStart(string text, int start)
    {
        if (start == 0) return 0;
        int end = start - 1;
        if (text[end] == '\n' && end > 0 && text[end - 1] == '\r') end--;
        return LineStart(text, end);
    }
}
