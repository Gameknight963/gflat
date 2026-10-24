using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public sealed record InterpolationPlan(PrefixedStringLiteralExpression Factory,
        IdentifierExpression Result, List<InterpolationPart> Parts);
    public sealed record InterpolationPart(CallExpression Action,
        CallExpression? Conversion = null, IdentifierExpression? Converted = null);
    private readonly Dictionary<InterpolatedStringExpression, InterpolationPlan> _interpolations = new();
    private int _interpolationTemporary;
    private readonly HashSet<CallExpression> primitiveInterpolationCalls = new();
    public InterpolationPlan GetInterpolation(InterpolatedStringExpression node) => _interpolations[node];

    private void CheckInterpolation(InterpolatedStringExpression node)
    {
        if (node.Prefix == null)
            throw new TypeCheckException("Interpolation requires a custom string prefix, for example s$\"{value}\"", node.Line);
        LiteralExpression Literal(string raw) => new(new Token(TokenKind.StringLiteral, node.Line, 0, 0, "\"" + raw + "\""), node.Line) { Span = node.Span };
        AstNode Size(int length) => new CastExpression(new NamedTypeExpression("nuint", null, node.Line),
            new LiteralExpression(new Token(TokenKind.ULongLiteral, node.Line, 0, 0, length + "ul"), node.Line), node.Line);
        var factory = new PrefixedStringLiteralExpression(node.Scope, node.Prefix, Literal(""), node.Line) { Span = node.Span };
        factory.Accept(this);
        TypeExpression resultType = GetType(factory);
        if (!TypeImplementsInterface(resultType, "ITextWriter"))
            throw new TypeCheckException($"String prefix '{node.Prefix}' must return a type implementing ITextWriter to support interpolation", node.Line);
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
                CallExpression action;
                CallExpression? conversion = null;
                IdentifierExpression? converted = null;
                if (part is LiteralExpression { Token.Kind: TokenKind.InterpolatedStringSegment } segment)
                {
                    string raw = segment.Token.Text;
                    action = new(new MemberAccessExpression(result, "Write", false, part.Line),
                        new() { Literal(raw), Size(StringLiteralEncoding.Bytes(raw, part.Line).Length) }, part.Line) { Span = part.Span };
                }
                else
                {
                    part.Accept(this);
                    TypeExpression type = ResolveAlias(GetType(part));
                    TypeExpression valueType = type is PointerTypeExpression ptr ? ptr.Inner : type is ManagedTypeExpression managed ? managed.Inner : type;
                    var destination = new UnaryExpression(result, TokenKind.Ampersand, true, part.Line) { Span = part.Span };
                    bool formattable = TypeImplementsInterface(type, "IFormattable") || valueType is NamedTypeExpression { Name: "IFormattable" };
                    if (formattable)
                        action = new(new MemberAccessExpression(part, "Format", false, part.Line), new() { destination }, part.Line) { Span = part.Span };
                    else if (type is NamedTypeExpression primitive && primitive.Name is
                        "bool" or "char" or "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "extralong" or "nint" or "nuint" or "float" or "double")
                    {
                        if (!_globalScope.Children.TryGetValue("std", out var std) || !std.Functions.TryGetValue("Format", out var first) ||
                            !(overloadFamilies.TryGetValue(first, out var family) ? family : new List<MethodDeclaration> { first })
                                .Any(m => m.Parameters.Count == 2 && ResolveAlias(m.Parameters[0].Type) is NamedTypeExpression parameter && parameter.Name == primitive.Name))
                        {
                            string source = primitive.Name == "extralong"
                                ? "std/core/IntegerFormatting.gf and std/core/ExtraLongFormatting.gf"
                                : "std/core/Formatting.gf (and std/libc/Formatting.gf for floating-point values)";
                            throw new TypeCheckException($"Primitive interpolation requires std::Format({primitive.Name}, ITextWriter*); include {source}", part.Line);
                        }
                        action = new(new NamespaceAccessExpression(new NamespaceAccessExpression(new GlobalExpression(part.Line), "std", part.Line), "Format", part.Line), new() { part, destination }, part.Line) { Span = part.Span };
                        primitiveInterpolationCalls.Add(action);
                    }
                    else if (TypeImplementsInterface(type, "IStringConvertable") || valueType is NamedTypeExpression { Name: "IStringConvertable" })
                    {
                        conversion = new(new MemberAccessExpression(part, "ToString", false, part.Line), new(), part.Line) { Span = part.Span };
                        conversion.Accept(this);
                        converted = Temporary(GetType(conversion));
                        action = new(new MemberAccessExpression(result, "Write", false, part.Line), new()
                        {
                            new MemberAccessExpression(converted, "Data", false, part.Line),
                            new MemberAccessExpression(converted, "Length", false, part.Line)
                        }, part.Line) { Span = part.Span };
                    }
                    else throw new TypeCheckException($"Interpolation value of type '{TypeName(type)}' must implement IFormattable or IStringConvertable; raw pointers are not implicitly treated as text", part.Line);
                }
                action.Accept(this);
                parts.Add(new(action, conversion, converted));
            }
            _interpolations[node] = new(factory, result, parts);
            RecordType(node, resultType);
        }
        finally { PopScope(); }
    }
}
