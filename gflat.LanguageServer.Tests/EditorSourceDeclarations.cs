using gflat.ast;

namespace gflat.LanguageServer.Tests;

internal static class EditorSourceDeclarations
{
    // Parse source declarations independently of checked/lowered callable metadata.
    // This keeps navigation expectations anchored to public extension members,
    // rather than same-named free forwarding functions or actual editor results.
    public static int ExtensionMethod(SourceFile source, string name, int? parameterCount = null,
        string? receiver = null, string? firstParameterType = null)
    {
        var tokens = Lexer.Tokenize(source);
        var unit = Parser.Parse(tokens);
        var matches = Blocks(unit.Members.Concat(unit.Namespaces)).SelectMany(block => block.Members
            .OfType<MethodDeclaration>().Where(method => method.Name == name &&
                (parameterCount == null || method.Parameters.Count == parameterCount) &&
                (receiver == null || Text(block.Target) == receiver) &&
                (firstParameterType == null || method.Parameters.Count > 0 && Text(method.Parameters[0].Type) == firstParameterType)))
            .ToArray();
        var selected = Assert.Single(matches);
        return tokens.First(token => token.Text == name && token.Start >= selected.ReturnType.Span.Start + selected.ReturnType.Span.Length &&
            token.Start < selected.Span.Start + selected.Span.Length).Start;

        string Text(TypeExpression type) => string.Concat(source.Text.Substring(type.Span.Start, type.Span.Length).Where(character => !char.IsWhiteSpace(character)));
        static IEnumerable<ExtensionDeclaration> Blocks(IEnumerable<AstNode> members)
        {
            foreach (var member in members) {
                if (member is ExtensionDeclaration block) yield return block;
                else if (member is NamespaceDeclaration ns)
                    foreach (var nested in Blocks(ns.Members)) yield return nested;
            }
        }
    }
}
