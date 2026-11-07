namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private int SymbolQueryOffset(string path, int offset)
    {
        static bool IsWord(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.Base or TokenKind.Override;
        var fileTokens = tokens[path];
        int current = TokenAt(path, offset);
        if (current >= 0 && IsWord(fileTokens[current].Kind)) return offset;
        int previous = TokenStartIndex(fileTokens, offset) - 1;
        return previous >= 0 && fileTokens[previous].End + 1 == offset && IsWord(fileTokens[previous].Kind)
            ? fileTokens[previous].Start : offset;
    }
}
