using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private IEnumerable<Symbol> BaseConstructors(string path, int offset)
    {
        var declaration = ContextParents(path, offset).FirstOrDefault(p => p.Node is ClassDeclaration);
        if (declaration?.Node is not ClassDeclaration type) return [];
        string? baseName = snapshot.Checker?.GetClassByIdentity(type.Name)?.BaseClass ?? type.BaseClass ??
            type.Interfaces.FirstOrDefault(name => symbols.Any(s => s.Syntax.Node is ClassDeclaration &&
                (s.Qualified == name || s.Name == name)));
        if (baseName == null) return [];
        string? owner = TypeOwner(new NamedTypeExpression(baseName, null, type.Line), path, offset);
        return symbols.Where(s => s.Owner == owner && s.Syntax.Node is ConstructorDeclaration && Accessible(s, path, offset));
    }

    private sealed record CompletionFilter(bool Restricted, Func<Symbol, bool> Allows);

    private CompletionFilter CompletionContext(string path, int index)
    {
        var ts = tokens[path];
        // Use tokens so this works while the declaration is incomplete and
        // parser recovery has not produced a type declaration yet.
        int start = index - 1;
        while (start >= 0 && ts[start].Kind is not (TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.Semicolon)) start--;
        start++;
        int withIndex = -1;
        for (int i = index - 1; i >= start; i--)
            if (ts[i].Text == "with") { withIndex = i; break; }
        if (withIndex > start && ts[withIndex - 1].Kind == TokenKind.CloseParen && Enumerable.Range(start, withIndex - start)
            .Any(i => ts[i].Kind == TokenKind.New && i + 1 < withIndex && ts[i + 1].Kind == TokenKind.Caret))
            return new(true, s => s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration or NamespaceDeclaration);
        if (start < index && ts[start].Kind == TokenKind.Using)
            return new(true, s => s.Syntax.Node is NamespaceDeclaration);

        int declaration = start;
        while (declaration < index && ts[declaration].Kind is TokenKind.Public or TokenKind.Private or TokenKind.Protected or TokenKind.Internal or TokenKind.Abstract) declaration++;
        if (declaration + 1 >= index || ts[declaration].Kind is not (TokenKind.Class or TokenKind.Struct) || ts[declaration + 1].Kind != TokenKind.Identifier)
            return new(false, _ => true);

        int colon = declaration + 2, depth = 0;
        for (; colon < index; colon++)
        {
            if (ts[colon].Kind == TokenKind.Less) depth++;
            else if (ts[colon].Kind == TokenKind.Greater) depth--;
            else if (ts[colon].Kind == TokenKind.Colon && depth == 0) break;
        }
        if (colon == index) return new(false, _ => true);

        var used = new HashSet<string>(StringComparer.Ordinal);
        int segment = colon + 1;
        for (int i = segment; i < index; i++)
            if (ts[i].Kind == TokenKind.Comma)
            {
                used.Add(string.Concat(ts[segment..i].Select(t => t.Text)));
                segment = i + 1;
            }
        var visible = Visible(path, ts[declaration].Start).ToArray();
        bool alreadyHasBase = visible.Any(s => s.Syntax.Node is ClassDeclaration && (used.Contains(s.Name) || used.Contains(s.Qualified))) ||
            symbols.Any(s => s.Syntax.Node is ClassDeclaration && used.Contains(s.Qualified));
        var self = symbols.FirstOrDefault(s => s.NameSpan.Source?.Path == path && s.NameSpan.Start == ts[declaration + 1].Start);
        return new(true, s => s.Syntax.Node is NamespaceDeclaration ||
            (s.Syntax.Node is InterfaceDeclaration || ts[declaration].Kind == TokenKind.Class && !alreadyHasBase && s.Syntax.Node is ClassDeclaration { IsGeneric: false }) &&
            s != self && s.Qualified != self?.Qualified && !used.Contains(s.Qualified) &&
            !visible.Any(v => v == s && used.Contains(v.Name)));
    }
}
