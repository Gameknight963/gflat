namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private int EscapedIdentifierOffset(string path, int offset)
    {
        string text = sources[path].Text;
        if (offset < 0 || offset >= text.Length - 1 || text[offset] != '@') return offset;
        int index = TokenAt(path, offset + 1);
        return index >= 0 && tokens[path][index].Kind == TokenKind.Identifier && tokens[path][index].Start == offset + 1
            ? offset + 1 : offset;
    }

    private int SymbolQueryOffset(string path, int offset)
    {
        offset = EscapedIdentifierOffset(path, offset);
        static bool IsWord(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.Base or TokenKind.Override;
        var fileTokens = tokens[path];
        int current = TokenAt(path, offset);
        if (current >= 0 && IsWord(fileTokens[current].Kind)) return offset;
        int previous = TokenStartIndex(fileTokens, offset) - 1;
        return previous >= 0 && fileTokens[previous].End + 1 == offset && IsWord(fileTokens[previous].Kind)
            ? fileTokens[previous].Start : offset;
    }
}
