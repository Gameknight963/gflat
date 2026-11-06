using System.Text;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    public string? MacroInspection(string path, int offset)
    {
        path = CanonicalQueryPath(path);
        var macro = MacroAt(path, offset);
        if (macro?.Expansion == null || macro.Definition) return null;
        var result = new StringBuilder(macro.Signature).AppendLine().AppendLine();
        int step = 1;
        foreach (var item in macro.Steps ?? [])
            result.Append(step++).Append(". ").AppendLine(item.Name).Append("   ").AppendLine(item.Before)
                .Append("   => ").AppendLine(item.After).AppendLine();
        result.AppendLine("Expanded tokens:").AppendLine(macro.FullExpansion ?? macro.Expansion);
        return result.ToString();
    }

    private IEnumerable<EditorCodeAction> MacroActions(string path, int offset)
    {
        var macro = MacroAt(path, offset);
        if (macro?.Expansion == null || macro.Definition) yield break;
        yield return new("Inspect macro expansion", "refactor.rewrite", Command:
            new("Inspect macro expansion", "gflat.inspectMacro", [Workspace.FileUri(path), offset]));
        if (macro.FullExpansion != null && macro.Invocation.Source != null)
        {
            // Raw groups suppress preprocessing of the inserted result once.
            // This preserves identifiers left by recursive expansion suppression.
            yield return Action("Expand macro inline", "refactor.inline", path,
                new SourceEdit(Workspace.ToRange(macro.Invocation), "@(" + macro.FullExpansion + ")"));
        }
    }
}
