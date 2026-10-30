using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class Parser
{
    private ExtensionDeclaration ParseExtensionDeclaration() => Located(() =>
    {
        int line = Consume().Line;
        var parameters = ParseGenericParameters();
        TypeExpression target = ParseTypeExpression(allowSignature: false);
        if (target is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression or FunctionPointerTypeExpression)
            throw new TypeCheckException("Extension blocks target the underlying object type, never a pointer or reference type", target.Span);
        Expect(TokenKind.OpenBrace);
        var members = new List<AstNode>();
        while (!Check(TokenKind.CloseBrace) && !Check(TokenKind.EndOfFile))
        {
            AstNode member = ParseMember();
            if (member is not MethodDeclaration method || method.IsStatic || method.IsVirtual || method.IsOverride ||
                method.IsAbstract || method.Body == null || method.StringLiteralPrefix != null || method.IsExplicitConversion ||
                method.ImplementationKind != FunctionImplementationKind.Ordinary)
                throw new TypeCheckException("Extension blocks contain ordinary instance method bodies", member.Span);
            members.Add(member);
        }
        Expect(TokenKind.CloseBrace);
        return new ExtensionDeclaration(target, parameters, members, line);
    });

    private MethodDeclaration ParseExplicitConversion(TokenKind accessibility, int line, List<AttributeNode> attributes, bool isConst)
    {
        Token marker = Consume(); // contextual 'explicit'
        Expect(TokenKind.Operator);
        TypeExpression target = ParseTypeExpression();
        var declaration = ParseMethodDeclaration(target, "$conversion$" + marker.Start, accessibility,
            true, false, false, false, line, false, isConst, attributes, new List<GenericParameter>());
        declaration.IsExplicitConversion = true;
        if (declaration.Parameters.Count != 1 || declaration.Parameters[0].DefaultValue != null ||
            declaration.Parameters[0].IsConst || declaration.Parameters[0].Type is ReferenceTypeExpression)
            throw new TypeCheckException("An explicit conversion requires exactly one ordinary value parameter", declaration.Span);
        return declaration;
    }
}
