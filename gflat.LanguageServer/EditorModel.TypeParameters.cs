using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private void RegisterTypeParameters()
    {
        // Generic definitions are intentionally not specialized for editor queries.
        // Parameters are source identities scoped to their declaring definition.
        foreach (var owner in symbols.ToArray())
        {
            var parameters = owner.Syntax.Node switch
            {
                MethodDeclaration method => method.GenericParameters,
                ClassDeclaration type => type.GenericParameters,
                StructDeclaration type => type.GenericParameters,
                InterfaceDeclaration type => type.GenericParameters,
                _ => null
            };
            if (parameters == null || parameters.Count == 0 || owner.NameSpan.Source is not { } source) continue;
            var ts = tokens[source.Path];
            int cursor = Array.FindIndex(ts, token => token.Start >= owner.NameSpan.Start + owner.NameSpan.Length);
            if (cursor < 0 || ts[cursor].Kind != TokenKind.Less) continue;
            cursor++;
            foreach (var parameter in parameters)
            {
                if (cursor >= ts.Length || ts[cursor].Kind != TokenKind.Identifier || ts[cursor].Text != parameter.Name) break;
                symbols.Add(new(parameter.Name, 25, "typeParameter", owner.Syntax, ts[cursor].Span,
                    owner.Namespace, owner.Owner, owner.Syntax, null, false, TokenKind.Public) { IsTypeParameter = true, TypeConstraint = parameter.Constraint });
                // A later parameter can also occur inside an earlier constraint.
                // Only a comma at the header's outer depth starts its declaration.
                int depth = 1;
                for (cursor++; cursor < ts.Length; cursor++)
                {
                    if (ts[cursor].Kind == TokenKind.Less) depth++;
                    else if (ts[cursor].Kind is TokenKind.Greater or TokenKind.GreaterGreater)
                        depth -= ts[cursor].Kind == TokenKind.GreaterGreater ? 2 : 1;
                    if (depth <= 0) break;
                    if (depth == 1 && ts[cursor].Kind == TokenKind.Comma) { cursor++; break; }
                }
            }
        }
    }
}
