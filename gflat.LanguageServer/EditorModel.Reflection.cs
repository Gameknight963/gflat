using gflat.ast;
using gflat.comptime;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<Token, Symbol> spliceReferences = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Symbol> reflectedDeclarationReferences = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> reflectionMemberTokens = new(ReferenceEqualityComparer.Instance);

    private TypeExpression? ReflectionExpressionType(AstNode node, string path, int offset)
    {
        var checkedType = snapshot.Checker?.GetType(node);
        if (checkedType != null && TypeChecker.TypeName(checkedType) != "<error>") return checkedType;
        return node switch
        {
            TypeofExpression or ReflectExpression => TypeChecker.ReflectionType(node.Line),
            IdentifierExpression identifier => Visible(path, offset, identifier.Name).FirstOrDefault(s => s.Name == identifier.Name)?.Type,
            MemberAccessExpression member when ReflectionExpressionType(member.Object, path, offset) is { } receiver && TypeChecker.IsReflectionType(receiver)
                => TypeChecker.ReflectionMemberType(member.Member, member.Line),
            IndexExpression index when ReflectionExpressionType(index.Target, path, offset) is ArrayTypeExpression array => array.ElementType,
            _ => null
        };
    }

    private Symbol? ReflectedSymbol(ConstValue.Reflection value)
    {
        if (value.Declaration is MethodDeclaration method)
            return SourceCallable(method) is { } sourceMethod ? sourceMethod with {
                BoundParameters = method.Parameters, BoundType = method.ReturnType, BoundMethod = method,
                Owner = value.DeclaringType == null ? sourceMethod.Owner : DisplayType(value.DeclaringType)
            } : null;
        if (value.Declaration is { } declaration)
        {
            var source = declarations.GetValueOrDefault(declaration) ?? symbols.FirstOrDefault(s =>
                s.Syntax.Node.GetType() == declaration.GetType() && s.Syntax.Span == declaration.Span && s.Name == value.Name);
            return source == null || value.Type == null ? source : source with {
                Type = value.Type, BoundType = value.Type,
                Owner = value.DeclaringType == null ? source.Owner : DisplayType(value.DeclaringType)
            };
        }
        if (value.Kind == "Type" && value.Type is NamedTypeExpression type &&
            snapshot.Checker?.GetTypeDeclaration(type.Name) is { } typeDeclaration)
            return declarations.GetValueOrDefault(typeDeclaration) ??
                sourceTypeDeclarations.GetValueOrDefault((typeDeclaration.GetType(), typeDeclaration.Span));
        return null;
    }

    private void IndexSpliceReferences()
    {
        if (snapshot.Checker is not { } checker) return;
        foreach (var syntax in snapshot.Nodes)
        {
            if (syntax.Node is ReflectExpression query && query.Target.Span.Source?.Path is { } queryPath &&
                checker.TryGetReflectionTarget(query, out var queried) && queried is { Kind: not "Type" } &&
                ReflectedSymbol(queried) is { } declaration)
            {
                var nameToken = tokens[queryPath].LastOrDefault(t => t.Kind == TokenKind.Identifier && t.Text == declaration.Name &&
                    t.Start >= query.Target.Span.Start && t.End < query.Target.Span.Start + query.Target.Span.Length);
                if (nameToken != null) reflectedDeclarationReferences[nameToken] = declaration;
            }
            if (syntax.Node is MemberAccessExpression member && syntax.Span.Source?.Path is { } memberPath &&
                ReflectionExpressionType(member.Object, memberPath, syntax.Span.Start) is { } receiverType && TypeChecker.IsReflectionType(receiverType))
            {
                var memberToken = tokens[memberPath].FirstOrDefault(t => t.Kind == TokenKind.Identifier &&
                    t.Text == member.Member && t.Start >= member.Object.Span.Start + member.Object.Span.Length &&
                    t.End < syntax.Span.Start + syntax.Span.Length);
                if (memberToken != null) reflectionMemberTokens.Add(memberToken);
            }
            AstNode? reflection = syntax.Node switch { SpliceExpression splice => splice.Reflection,
                SpliceTypeExpression splice => splice.Reflection, _ => null };
            if (reflection is not IdentifierExpression || syntax.Span.Source?.Path is not { } path ||
                !checker.TryGetConstValue(reflection, out var constant) || constant is not ConstValue.Reflection value ||
                ReflectedSymbol(value) is not { } symbol) continue;
            var token = tokens[path].FirstOrDefault(t => t.Start == reflection.Span.Start && t.Kind == TokenKind.Identifier);
            if (token != null) spliceReferences[token] = symbol;
        }
    }

    private AstNode? ReflectionReceiver(string path, int separatorIndex, bool insertedSeparator = false)
    {
        if (separatorIndex < 1 || !insertedSeparator && (separatorIndex >= tokens[path].Length || tokens[path][separatorIndex].Kind != TokenKind.Dot)) return null;
        int end = tokens[path][separatorIndex - 1].End + 1;
        return nodes[path].Where(n => n.Span.Start + n.Span.Length == end &&
            n.Node is IdentifierExpression or MemberAccessExpression or IndexExpression or ReflectExpression or TypeofExpression)
            .OrderByDescending(n => n.Span.Length).Select(n => n.Node)
            .FirstOrDefault(node => ExpressionType(node, path, end) is { } type && TypeChecker.IsReflectionType(type));
    }

    private object? ReflectionCompletion(string path, int offset, int index, TextRange range, bool insertedSeparator = false)
    {
        if (snapshot.Checker is not { } checker || ReflectionReceiver(path, index - 1, insertedSeparator) is not { } receiver) return null;
        checker.TryGetConstValue(receiver, out var constant);
        var value = constant as ConstValue.Reflection;
        var names = new List<string> { "Name", "QualifiedName", "Kind", "IsType" };
        if (value == null || value.Type != null) names.Add("Type");
        if (value?.DeclaringType != null) names.Add("DeclaringType");
        if (value?.Declaration is FieldDeclaration field)
        {
            names.AddRange(["IsStatic", "IsConst", "Accessibility", "IsPublic", "IsPrivate"]);
            if (value.DeclaringType != null && !field.IsStatic && !field.IsConst) names.Add("Offset");
        }
        if (value?.Declaration is AliasDeclaration) names.AddRange(["Target", "IsConstrained", "Constraints"]);
        if (value?.Declaration is VariableDeclaration) names.Add("IsConst");
        if (value?.Overloads != null) names.Add("Overloads");
        if (value?.Declaration is Parameter) names.AddRange(["Position", "IsConst", "IsRef", "HasDefaultValue"]);
        if (value == null || value.Kind == "Type")
        {
            names.AddRange(["HasLayout", "Size", "Alignment", "IsReadOnly", "IsImmutable", "IsPointer", "IsManaged",
                "IsNullable", "HasDestructor", "HasFixedLength", "HasElementType", "IsStruct", "IsClass", "IsInterface",
                "IsEnum", "IsPrimitive", "IsArray", "IsReference", "IsFunctionPointer", "IsFunctionSignature",
                "Fields", "StaticFields", "Methods", "Bases"]);
            if (value == null || value.Type is PointerTypeExpression or ManagedTypeExpression or ArrayTypeExpression or ReferenceTypeExpression)
                names.Add("ElementType");
            if (value?.Type is ArrayTypeExpression { Size: not null }) names.Add("Length");
            if (value?.Type is ManagedTypeExpression) names.Add("CollectorType");
        }
        if (value?.Declaration is MethodDeclaration || value?.Type is FunctionPointerTypeExpression)
            names.AddRange(["ReturnType", "ParameterTypes", "ParameterCount"]);
        if (value?.Declaration is MethodDeclaration) names.AddRange(["IsStatic", "Parameters", "Accessibility", "IsPublic",
            "IsPrivate", "IsConst", "IsReadOnly", "IsImmutable", "IsNoThrow", "IsVirtual", "IsOverride", "IsAbstract"]);
        return new { isIncomplete = false, items = names.Distinct().Select(name => new {
            label = name, kind = 10, detail = "reflection." + name, textEdit = new { range, newText = name }
        }).ToArray() };
    }

    private object? ReflectionMemberHover(string path, int offset, VisualStudioHover? presentation)
    {
        if (snapshot.Checker is not { } checker) return null;
        var syntax = nodes[path].Where(n => n.Node is MemberAccessExpression member &&
            offset >= member.Object.Span.Start + member.Object.Span.Length && Contains(n, offset) &&
            ReflectionExpressionType(member.Object, path, offset) is { } receiverType && TypeChecker.IsReflectionType(receiverType)).MinBy(n => n.Span.Length);
        if (syntax == null) return null;
        if (!checker.TryGetConstValue(syntax.Node, out var value))
        {
            var member = (MemberAccessExpression)syntax.Node;
            if (TypeChecker.ReflectionMemberType(member.Member, member.Line) is not { } memberType) return null;
            DisplayPart[] signature = [.. TypeDisplayParts(memberType, path, offset), new("text", " "),
                new("keyword", "reflection"), new("punctuation", "."), new("property", member.Member)];
            return new HoverResult(new("markdown", "```gflat\n" + string.Concat(signature.Select(p => p.Text)) + "\n```"),
                Workspace.ToRange(syntax.Span), presentation?.Render(signature, [], "property.public"));
        }
        var parts = new List<DisplayPart>();
        parts.Add(new("punctuation", "("));
        parts.AddRange(TypeDisplayParts(checker.GetType(syntax.Node), path, offset));
        parts.Add(new("punctuation", ")"));
        if (value is ConstValue.Reflection { Kind: "Type", Type: not null } reflectedType)
            parts.AddRange(TypeDisplayParts(reflectedType.Type, path, offset));
        else parts.Add(new(value is ConstValue.String ? "string" : value is ConstValue.Boolean ? "keyword" : value is ConstValue.Reflection ? "text" : "number", value switch {
            ConstValue.String text => System.Text.Json.JsonSerializer.Serialize(text.Value),
            ConstValue.Integer number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ConstValue.UInteger number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ConstValue.Boolean boolean => boolean.Value ? "true" : "false",
            ConstValue.Reflection reflection => reflection.Name,
            ConstValue.Array array => $"{array.Elements.Count} items",
            _ => value?.ToString() ?? ""
        }));
        string preview = string.Concat(parts.Select(p => p.Text));
        return new HoverResult(new("markdown", "```gflat\n" + preview + "\n```"), Workspace.ToRange(syntax.Span),
            presentation?.Render(parts.ToArray(), [], "constant.public"));
    }

    private string ReflectedTypeKind(TypeExpression type) => type switch {
        PointerTypeExpression => "Pointer", ManagedTypeExpression => "Managed", ArrayTypeExpression => "Array",
        ReferenceTypeExpression => "Reference", FunctionPointerTypeExpression => "Function",
        NamedTypeExpression named when snapshot.Checker?.GetStructByIdentity(named.Name) != null => "Struct",
        NamedTypeExpression named when snapshot.Checker?.GetClassByIdentity(named.Name) != null => "Class",
        NamedTypeExpression named when snapshot.Checker?.GetInterface(named.Name) != null => "Interface",
        NamedTypeExpression named when snapshot.Checker?.ResolveEnum(named) != null => "Enum",
        _ => "Primitive"
    };
}
