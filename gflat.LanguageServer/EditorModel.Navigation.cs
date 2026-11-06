using gflat.ast;

namespace gflat.LanguageServer;

public sealed record TypeHierarchyData(string Path, int Offset);
public sealed record TypeHierarchyItem(string Name, int Kind, string Uri, TextRange Range, TextRange SelectionRange, TypeHierarchyData Data);

public sealed partial class EditorModel
{
    private Symbol? NavigationSymbol(string path, int offset)
    {
        path = CanonicalQueryPath(path);
        int index = TokenAt(path, offset);
        return index < 0 ? null : Resolve(path, index);
    }

    private IEnumerable<Symbol> DirectBaseSymbols(Symbol type)
    {
        IEnumerable<TypeExpression> bases = type.Syntax.Node switch
        {
            ClassDeclaration c => c.BaseTypes,
            StructDeclaration s => s.BaseTypes,
            _ => []
        };
        foreach (var baseType in bases)
        {
            string? owner = TypeOwner(baseType, type.NameSpan.Source!.Path, type.NameSpan.Start);
            var symbol = symbols.FirstOrDefault(s => !s.IsTypeParameter && IsType(s.Syntax.Node) && s.Qualified == owner);
            if (symbol != null) yield return symbol;
        }
    }

    private IEnumerable<Symbol> AncestorTypes(Symbol type)
    {
        var seen = new HashSet<SourceSpan>();
        var pending = new Queue<Symbol>(DirectBaseSymbols(type));
        while (pending.TryDequeue(out var next))
        {
            if (!seen.Add(next.NameSpan)) continue;
            yield return next;
            foreach (var parent in DirectBaseSymbols(next)) pending.Enqueue(parent);
        }
    }

    private bool ContractMember(Symbol symbol) => symbol.Syntax.Node is MethodDeclaration { IsVirtual: true } or
        MethodDeclaration { IsOverride: true } or MethodDeclaration { IsAbstract: true } ||
        symbols.Any(s => s.Syntax.Node is InterfaceDeclaration && s.Qualified == symbol.Owner);

    private bool SameMember(Symbol left, Symbol right)
    {
        if (left.Name != right.Name || left.Static != right.Static || left.Syntax.Node.GetType() != right.Syntax.Node.GetType()) return false;
        if (left.Parameters == null || right.Parameters == null) return left.Parameters == null && right.Parameters == null;
        if (left.Parameters.Count != right.Parameters.Count) return false;
        bool ParametersMatch(Symbol first, Symbol second) => first.Parameters!.Select(p => DisplayType(p.Type)).SequenceEqual(second.Parameters!.Select(p => DisplayType(p.Type)));
        if (ContractMember(left) && ContractMember(right) || ParametersMatch(left, right)) return true;
        bool ProjectedMatch(Symbol contract, Symbol implementation)
        {
            var declaration = symbols.FirstOrDefault(s => s.Syntax.Node is InterfaceDeclaration && s.Qualified == contract.Owner);
            var owner = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == implementation.Owner);
            return declaration != null && owner != null && ContractMembers(owner, declaration).Any(member =>
                member.NameSpan == contract.NameSpan && member.Parameters != null && ParametersMatch(member, implementation));
        }
        return ProjectedMatch(left, right) || ProjectedMatch(right, left);
    }

    private Symbol? OverriddenMember(string path, int offset)
    {
        int index = TokenAt(path, offset);
        if (index < 0 || tokens[path][index].Kind != TokenKind.Override) return null;
        var member = ContextParents(path, offset).Select(s => declarations.GetValueOrDefault(s.Node))
            .FirstOrDefault(s => s?.Syntax.Node is MethodDeclaration or PropertyDeclaration);
        var owner = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == member?.Owner);
        if (member == null || owner == null) return null;
        foreach (var parent in AncestorTypes(owner).Where(s => s.Syntax.Node is ClassDeclaration))
        {
            var candidate = symbols.FirstOrDefault(s => s.Owner == parent.Qualified && SameMember(member, s));
            if (candidate != null) return candidate;
        }
        return null;
    }

    private static Location? NavigationLocation(Symbol symbol) => symbol.NameSpan.Source is { } source && !source.Path.StartsWith('<')
        ? new(Workspace.FileUri(source.Path), Workspace.ToRange(symbol.NameSpan)) : null;

    public Location[] Implementations(string path, int offset)
    {
        var selected = NavigationSymbol(path, offset);
        if (selected == null) return [];
        var owner = IsType(selected.Syntax.Node) ? selected : symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == selected.Owner);
        if (owner == null) return [];
        var types = symbols.Where(s => !s.IsTypeParameter && IsType(s.Syntax.Node) &&
            (s.NameSpan == owner.NameSpan || AncestorTypes(s).Any(parent => parent.NameSpan == owner.NameSpan))).ToArray();
        IEnumerable<Symbol> matches = IsType(selected.Syntax.Node)
            ? types.Where(s => s.Syntax.Node is StructDeclaration or ClassDeclaration { IsAbstract: false })
            : types.SelectMany(type => symbols.Where(s => s.Owner == type.Qualified && SameMember(selected, s) &&
                s.Syntax.Node is MethodDeclaration { Body: not null } or PropertyDeclaration));
        return matches.Select(NavigationLocation).OfType<Location>().Distinct().ToArray();
    }

    private TypeHierarchyItem HierarchyItem(Symbol type) => new(type.Name, type.Syntax.Node is InterfaceDeclaration ? 11 :
        type.Syntax.Node is StructDeclaration ? 23 : 5, Workspace.FileUri(type.NameSpan.Source!.Path),
        Workspace.ToRange(type.Syntax.Span), Workspace.ToRange(type.NameSpan), new(type.NameSpan.Source.Path, type.NameSpan.Start));

    public TypeHierarchyItem[] PrepareTypeHierarchy(string path, int offset)
    {
        path = CanonicalQueryPath(path);
        var selected = NavigationSymbol(path, offset);
        if (selected != null && !IsType(selected.Syntax.Node) && selected.Type != null)
        {
            string? owner = TypeOwner(selected.Type, path, offset);
            selected = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == owner);
        }
        return selected != null && IsType(selected.Syntax.Node) && NavigationLocation(selected) != null ? [HierarchyItem(selected)] : [];
    }

    public TypeHierarchyItem[] TypeHierarchy(string path, int offset, bool subtypes)
    {
        var selected = NavigationSymbol(path, offset);
        if (selected == null || !IsType(selected.Syntax.Node)) return [];
        var matches = subtypes ? symbols.Where(s => !s.IsTypeParameter && IsType(s.Syntax.Node) &&
            DirectBaseSymbols(s).Any(parent => parent.NameSpan == selected.NameSpan)) : DirectBaseSymbols(selected);
        return matches.Where(s => NavigationLocation(s) != null).DistinctBy(s => s.NameSpan).Select(HierarchyItem).ToArray();
    }
}
