using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public sealed record InterpolationPart(CallExpression Append, CallExpression? Convert,
        IdentifierExpression? Text, IdentifierExpression? Length);
    public sealed record InterpolationPlan(PrefixedStringLiteralExpression Factory,
        IdentifierExpression Result, List<InterpolationPart> Parts);
    private readonly Dictionary<InterpolatedStringExpression, InterpolationPlan> _interpolations = new();
    private int _interpolationTemporary;
    public InterpolationPlan GetInterpolation(InterpolatedStringExpression node) => _interpolations[node];

    private void CheckInterpolation(InterpolatedStringExpression node)
    {
        if (node.Prefix == null || node.Prefix == "c")
            throw new TypeCheckException("Interpolation requires a custom string prefix, for example s$\"{value}\"", node.Line);
        LiteralExpression Literal(string raw) => new(new Token(TokenKind.StringLiteral, node.Line, 0, 0, "\"" + raw + "\""), node.Line) { Span = node.Span };
        AstNode Size(int length) => new CastExpression(new NamedTypeExpression("nuint", null, node.Line),
            new LiteralExpression(new Token(TokenKind.ULongLiteral, node.Line, 0, 0, length + "ul"), node.Line), node.Line);
        var factory = new PrefixedStringLiteralExpression(node.Scope, node.Prefix, Literal(""), node.Line) { Span = node.Span };
        factory.Accept(this);
        TypeExpression resultType = GetType(factory);
        if (!TypeImplementsInterface(resultType, "IInterpolatedString"))
            throw new TypeCheckException($"String prefix '{node.Prefix}' must return a type implementing IInterpolatedString to support interpolation", node.Line);
        PushScope();
        try
        {
            IdentifierExpression Temporary(TypeExpression type)
            {
                var id = new IdentifierExpression("$interpolation$" + _interpolationTemporary++, node.Line) { Span = node.Span };
                DeclareVariable(id.Name, type, node.Line);
                id.Accept(this);
                return id;
            }
            var result = Temporary(resultType);
            var parts = new List<InterpolationPart>();
            foreach (AstNode part in node.Parts)
            {
                using var context = SourceContext.Enter(part.Span);
                CallExpression? convert = null;
                IdentifierExpression? text = null, length = null;
                List<AstNode> args;
                if (part is LiteralExpression { Token.Kind: TokenKind.InterpolatedStringSegment } segment)
                {
                    string raw = segment.Token.Text;
                    args = new() { Literal(raw), Size(StringLiteralEncoding.Bytes(raw, part.Line).Length) };
                }
                else
                {
                    part.Accept(this);
                    TypeExpression type = ResolveAlias(GetType(part));
                    TypeExpression valueType = type is PointerTypeExpression ptr ? ptr.Inner : type is ManagedTypeExpression managed ? managed.Inner : type;
                    bool convertible = TypeImplementsInterface(type, "IStringConvertible") ||
                        valueType is NamedTypeExpression { Name: "IStringConvertible" };
                    if (convertible)
                        convert = new(new MemberAccessExpression(part, "ToString", false, part.Line), new(), part.Line) { Span = part.Span };
                    else if (type is NamedTypeExpression primitive && primitive.Name is
                        "bool" or "char" or "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or "float" or "double")
                    {
                        if (!_globalScope.Children.TryGetValue("std", out var std) || !std.Functions.TryGetValue("ToString", out var first) ||
                            !(overloadFamilies.TryGetValue(first, out var family) ? family : new List<MethodDeclaration> { first })
                                .Any(m => m.Parameters.Count == 1 && ResolveAlias(m.Parameters[0].Type) is NamedTypeExpression parameter && parameter.Name == primitive.Name))
                            throw new TypeCheckException($"Primitive interpolation requires std::ToString({primitive.Name}); include std/core/Formatting.gf (and std/libc/Formatting.gf for floating-point values)", part.Line);
                        convert = new(new NamespaceAccessExpression(new NamespaceAccessExpression(new GlobalExpression(part.Line), "std", part.Line), "ToString", part.Line), new() { part }, part.Line) { Span = part.Span };
                    }
                    else
                        throw new TypeCheckException($"Interpolation value of type '{TypeName(type)}' must implement IStringConvertible; raw pointers are not implicitly treated as text", part.Line);
                    convert.Accept(this);
                    if (ResolveAlias(GetType(convert)) is not PointerTypeExpression { IsReadOnly: false, IsNullable: false, Inner: NamedTypeExpression { Name: "char" } })
                        throw new TypeCheckException("Interpolation ToString must return an owned, non-null char*", part.Line);
                    text = Temporary(GetType(convert));
                    length = Temporary(new NamedTypeExpression("nuint", null, part.Line));
                    args = new() { text, length };
                }
                var append = new CallExpression(new MemberAccessExpression(result, "AppendLiteral", false, part.Line), args, part.Line) { Span = part.Span };
                append.Accept(this);
                parts.Add(new(append, convert, text, length));
            }
            _interpolations[node] = new(factory, result, parts);
            RecordType(node, resultType);
        }
        finally { PopScope(); }
    }
}
