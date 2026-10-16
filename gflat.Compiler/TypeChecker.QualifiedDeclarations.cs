using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    // Bind explicit owners before collecting symbols. A qualified declaration has
    // exactly the same enclosing scope as a declaration written inside that owner.
    private void BindQualifiedDeclarations(CompilationUnit unit)
    {
        var scopes = new Dictionary<string, (List<AstNode> Members, AstNode? Owner)>();
        var pending = new List<(List<AstNode> Members, string Scope, AstNode Declaration, string Name)>();
        Index(unit.Members, "", null);
        foreach (var ns in unit.Namespaces) Index(ns.Members, ns.Name, ns);
        foreach (var entry in pending)
        {
            int separator = entry.Name.LastIndexOf("::", StringComparison.Ordinal);
            string ownerName = entry.Name[..separator];
            string memberName = entry.Name[(separator + 2)..];
            var search = entry.Scope;
            (List<AstNode> Members, AstNode? Owner) target;
            while (!scopes.TryGetValue(search.Length == 0 ? ownerName : search + "::" + ownerName, out target))
            {
                if (search.Length == 0)
                    throw new TypeCheckException($"Unknown declaration owner '{ownerName}'", entry.Declaration.Span);
                int parent = search.LastIndexOf("::", StringComparison.Ordinal);
                search = parent < 0 ? "" : search[..parent];
            }
            if (target.Owner is ClassDeclaration or StructDeclaration)
            {
                // External definitions implement declarations; they cannot add
                // members to a closed type from elsewhere in the program.
                if (entry.Declaration is not MethodDeclaration method ||
                    !target.Members.OfType<MethodDeclaration>().Any(m => m.Name == memberName &&
                        m.ImplementationKind is FunctionImplementationKind.Weak or FunctionImplementationKind.Expect))
                    throw new TypeCheckException($"Qualified type member '{entry.Name}' requires an expect or weak declaration in its owning type", entry.Declaration.Span);
                if (!method.IsStatic)
                    throw new TypeCheckException("Qualified definitions of type members must be static", method.Span);
            }
            switch (entry.Declaration)
            {
                case MethodDeclaration method: method.Name = memberName; break;
                case AliasDeclaration alias: alias.Name = memberName; break;
            }
            entry.Members.Remove(entry.Declaration);
            target.Members.Add(entry.Declaration);
        }

        void Index(List<AstNode> members, string scope, AstNode? owner)
        {
            // Multiple namespace blocks share the same destination scope.
            scopes.TryAdd(scope, (members, owner));
            foreach (var node in members)
            {
                if (node is AliasDeclaration binding && owner is ClassDeclaration or StructDeclaration &&
                    binding.ImplementationKind != FunctionImplementationKind.Ordinary)
                    throw new TypeCheckException("Provider alias declarations must be declared in a namespace", binding.Span);
                if (node is AliasDeclaration ordinaryAlias && owner is ClassDeclaration or StructDeclaration)
                {
                    // Continue indexing unrelated declarations during editor recovery.
                    AnalyzeSafely(() => throw new TypeCheckException(
                        "Type aliases inside classes or structs are not supported; declare the alias in a namespace or local scope",
                        ordinaryAlias.Span));
                    continue;
                }
                if (node is AliasDeclaration { TargetType: not null } bindingTarget && owner is not (ClassDeclaration or StructDeclaration))
                    aliasTargetNamespaces[bindingTarget.TargetType] = scope;
                string? qualified = node switch { MethodDeclaration m => m.Name, AliasDeclaration a => a.Name, _ => null };
                if (qualified?.Contains("::", StringComparison.Ordinal) == true)
                    pending.Add((members, scope, node, qualified));
                switch (node)
                {
                    case NamespaceDeclaration ns: Index(ns.Members, Join(scope, ns.Name), ns); break;
                    case ClassDeclaration cls: Index(cls.Members, Join(scope, cls.SourceName), cls); break;
                    case StructDeclaration str: Index(str.Members, Join(scope, str.SourceName), str); break;
                }
            }
        }
        static string Join(string scope, string name) => scope.Length == 0 ? name : scope + "::" + name;
    }
}
