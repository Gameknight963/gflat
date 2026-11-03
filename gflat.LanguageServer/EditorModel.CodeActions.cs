using System.Text;
using gflat.ast;

namespace gflat.LanguageServer;

public sealed record ActionWorkspaceEdit(Dictionary<string, SourceEdit[]> Changes);
public sealed record EditorCommand(string Title, string Command, object[] Arguments);
public sealed record EditorCodeAction(string Title, string Kind, ActionWorkspaceEdit? Edit = null, EditorCommand? Command = null);

public sealed partial class EditorModel
{
    private EditorCodeAction Action(string title, string kind, string path, params SourceEdit[] edits)
        => new(title, kind, new(new() { [Workspace.FileUri(path)] = edits }));

    public EditorCodeAction[] CodeActions(string path, TextRange range)
    {
        var source = sources[path];
        int offset = Workspace.Offset(source.Text, System.Text.Json.JsonSerializer.SerializeToElement(range.Start, Protocol.JsonOptions));
        var actions = new List<EditorCodeAction>();
        actions.AddRange(MacroActions(path, offset));
        if (ExportDocumentation().Length > 0)
            actions.Add(new("Generate documentation manual", "source", Command:
                new("Generate documentation manual", "gflat.generateDocumentation", [Workspace.FileUri(path)])));
        var contexts = HeaderContexts(path);
        if (contexts.Length > 1)
            foreach (var context in contexts)
                actions.Add(new("Use header context: " + context.Label, "refactor.rewrite", Command:
                    new("Use header context", "gflat.selectHeaderContext", [Workspace.FileUri(path), context.Id])));
        var imports = new List<(string Name, SourceSpan Span)>();
        var fileTokens = tokens[path];
        for (int i = 0; i < fileTokens.Length; i++)
            if (fileTokens[i].Kind == TokenKind.Using)
            {
                int lineStart = source.Text.LastIndexOf('\n', Math.Max(0, fileTokens[i].Start - 1)) + 1;
                if (source.Text.AsSpan(lineStart, fileTokens[i].Start - lineStart).Contains('#')) continue;
                int end = i + 1;
                while (end < fileTokens.Length && fileTokens[end].Kind is TokenKind.Identifier or TokenKind.DoubleColon) end++;
                if (end < fileTokens.Length && fileTokens[end].Kind == TokenKind.Semicolon)
                {
                    string name = string.Concat(fileTokens[(i + 1)..end].Select(t => t.Text));
                    if ((snapshot.Imports.GetValueOrDefault(source.Id) ?? []).Contains(name))
                        imports.Add((name, source.Span(fileTokens[i].Start, fileTokens[end].End - fileTokens[i].Start + 1)));
                }
            }
        if (imports.Count > 0 && !snapshot.Diagnostics.Any(d => d.Severity == diagnostics.DiagnosticSeverity.Error))
        {
            EnsureReferences();
            foreach (var import in imports)
            {
                string name = import.Name;
                bool used = references.Any(pair => pair.Key.Source?.Path == path && pair.Value != null &&
                    !declarationsBySpan.ContainsKey((path, pair.Key.Start, pair.Key.Span.Length)) &&
                    (pair.Value.Namespace == name || pair.Value.Owner == name || pair.Value.Qualified == name) &&
                    !imports.Any(i => pair.Key.Start >= i.Span.Start && pair.Key.Start < i.Span.Start + i.Span.Length));
                if (used) continue;
                int end = import.Span.Start + import.Span.Length;
                while (end < source.Text.Length && source.Text[end] is ' ' or '\t') end++;
                if (end < source.Text.Length && source.Text[end] == '\r') end++;
                if (end < source.Text.Length && source.Text[end] == '\n') end++;
                actions.Add(Action("Remove unused using " + name, "source.organizeImports", path,
                    new SourceEdit(Workspace.ToRange(source.Span(import.Span.Start, end - import.Span.Start)), "")));
            }
        }
        int tokenIndex = TokenAt(path, offset);
        if (tokenIndex >= 0 && tokens[path][tokenIndex].Kind == TokenKind.Identifier && Resolve(path, tokenIndex) == null)
        {
            string name = tokens[path][tokenIndex].Text;
            foreach (string ns in symbols.Where(s => s.Name == name && s.Owner == null && s.Scope == null && s.Namespace.Length > 0 &&
                s.Syntax.Node is not NamespaceDeclaration && Accessible(s, path, offset)).Select(s => s.Namespace).Distinct())
            {
                if ((snapshot.Imports.GetValueOrDefault(source.Id) ?? []).Contains(ns)) continue;
                string newline = source.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                actions.Add(Action("Add using " + ns, "quickfix", path, new SourceEdit(new(new(0, 0), new(0, 0)), "using " + ns + ";" + newline + (imports.Count == 0 ? newline : ""))));
            }
        }
        var owner = symbols.Where(s => s.NameSpan.Source?.Path == path && s.Syntax.Node is ClassDeclaration or StructDeclaration &&
            offset >= s.Syntax.Span.Start && offset < s.Syntax.Span.Start + s.Syntax.Span.Length)
            .MinBy(s => s.Syntax.Span.Length);
        if (owner != null)
        {
            var close = tokens[path].LastOrDefault(t => t.Kind == TokenKind.CloseBrace && t.Start >= owner.Syntax.Span.Start &&
                t.Start < owner.Syntax.Span.Start + owner.Syntax.Span.Length);
            if (close != null)
            {
                string newline = source.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string stub = "throw new Exception(\"Not implemented\");";
                string indent = new(' ', 4 * (Parents(owner.Syntax).Count(p => p.Node is ClassDeclaration or StructDeclaration or NamespaceDeclaration) + 1));
                string MethodStub(Symbol member)
                {
                    var method = (MethodDeclaration)member.Syntax.Node;
                    string generics = method.GenericParameters.Count == 0 ? "" : "<" + string.Join(", ", method.GenericParameters.Select(p => p.Name +
                        (p.Constraint == null ? "" : " : " + DisplayType(p.Constraint)))) + ">";
                    string signature = "public " + (method.IsStatic ? "static " : "") + (method.IsImmutable ? "immutable " : method.IsReadOnly ? "readonly " : "") +
                        (method.IsRefReturn ? "ref " : "") + DisplayType(member.BoundType ?? method.ReturnType) + " " + member.Name + generics + "(" +
                        string.Join(", ", (member.Parameters ?? []).Select(ParameterText)) + ")" + (method.IsNoThrow ? " nothrow" : "");
                    return indent + signature + newline + indent + "{" + newline + indent + "    " + stub + newline + indent + "}";
                }
                foreach (var contract in AncestorTypes(owner).Where(s => s.Syntax.Node is InterfaceDeclaration))
                {
                    var contractMembers = ContractMembers(owner, contract).ToArray();
                    var missing = contractMembers.Where(s => s.Syntax.Node is MethodDeclaration or PropertyDeclaration &&
                        !TypeMembers(owner.Qualified).Any(existing => SameMember(s, existing))).ToArray();
                    if (missing.Length == 0) continue;
                    string MemberStub(Symbol member)
                    {
                        if (member.Syntax.Node is not PropertyDeclaration property) return MethodStub(member);
                        var result = new StringBuilder(indent + "public " + (property.IsRef ? "ref " : "") + DisplayType(member.BoundType ?? property.Type) + " " + property.Name + newline + indent + "{" + newline);
                        foreach (var accessor in new[] { ("get", property.Getter), ("set", property.Setter) })
                            if (accessor.Item2 is { } method)
                                result.Append(indent + "    " + (method.IsImmutable ? "immutable " : method.IsReadOnly ? "readonly " : "") + accessor.Item1 +
                                    (method.IsNoThrow ? " nothrow" : "") + newline + indent + "    {" + newline + indent + "        " + stub + newline + indent + "    }" + newline);
                        return result.Append(indent + "}").ToString();
                    }
                    string generated = newline + string.Join(newline + newline, missing.Select(MemberStub)) + newline;
                    actions.Add(Action("Implement " + contract.Name, "quickfix", path,
                        new SourceEdit(Workspace.ToRange(source.Span(close.Start, 0)), generated)));
                }
                var fields = symbols.Where(s => s.Owner == owner.Qualified && s.Syntax.Node is FieldDeclaration { IsStatic: false, IsConst: false, PropertyName: null }).ToArray();
                if (fields.Length > 0 && !symbols.Any(s => s.Owner == owner.Qualified && s.Syntax.Node is ConstructorDeclaration &&
                    s.Parameters?.Select(p => DisplayType(p.Type)).SequenceEqual(fields.Select(f => DisplayType(f.Type!))) == true))
                {
                    string generated = newline + indent + "public " + owner.Name + "(" + string.Join(", ", fields.Select(f => DisplayType(f.Type!) + " " + f.Name)) + ")" +
                        newline + indent + "{" + newline + string.Join(newline, fields.Select(f => indent + "    (*this)." + f.Name + " = move " + f.Name + ";")) +
                        newline + indent + "}" + newline;
                    actions.Add(Action("Generate constructor", "refactor.rewrite", path, new SourceEdit(Workspace.ToRange(source.Span(close.Start, 0)), generated)));
                }
            }
        }
        return actions.ToArray();
    }

    private IEnumerable<Symbol> ContractMembers(Symbol owner, Symbol contract)
    {
        var bases = owner.Syntax.Node switch { ClassDeclaration c => c.BaseTypes, StructDeclaration s => s.BaseTypes, _ => [] };
        var actual = bases.FirstOrDefault(b => TypeOwner(b, owner.NameSpan.Source!.Path, owner.NameSpan.Start) == contract.Qualified);
        if (actual != null && snapshot.Checker?.GetSourceTypeBinding(actual) is NamedTypeExpression bound && snapshot.Checker.GetInterface(bound.Name) is { } info)
        {
            foreach (var method in info.Methods)
                if (method.PropertyName == null && SourceCallable(method) is { } declaration)
                    yield return declaration with { BoundParameters = method.Parameters, BoundType = method.ReturnType };
            foreach (var property in symbols.Where(s => s.Owner == contract.Qualified && s.Syntax.Node is PropertyDeclaration)) yield return property;
        }
        else foreach (var member in symbols.Where(s => s.Owner == contract.Qualified && s.Syntax.Node is MethodDeclaration or PropertyDeclaration)) yield return member;
    }
}
