using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<string, string> _displayNames = new();

    public string DisplayName(string name)
    {
        if (_displayNames.TryGetValue(name, out var display)) return display;
        // Nested declarations use a dot internally, including nested types
        // inside generic specializations. Preserve the specialization's spelling.
        int dot = name.LastIndexOf('.');
        if (dot >= 0) return DisplayName(name[..dot]) + "::" + name[(dot + 1)..];
        return name;
    }

    private enum InheritedTypeProtection { None, ShallowInline, DeepImmutable }

    private static bool NeedsTypeProtectionWrapper(TypeExpression type, InheritedTypeProtection inherited)
        => type.IsReadOnlyValue && inherited != InheritedTypeProtection.DeepImmutable
            && (inherited != InheritedTypeProtection.ShallowInline || type.IsImmutableValue);

    private static InheritedTypeProtection PrintedTypeProtection(TypeExpression type)
        => type.IsImmutableValue ? InheritedTypeProtection.DeepImmutable
            : type is ArrayTypeExpression ? InheritedTypeProtection.ShallowInline : InheritedTypeProtection.None;

    private static InheritedTypeProtection IndirectTypeProtection(InheritedTypeProtection inherited)
        => inherited == InheritedTypeProtection.DeepImmutable ? inherited : InheritedTypeProtection.None;

    public string DisplayTypeName(TypeExpression type) => DisplayTypeName(type, InheritedTypeProtection.None);
    // An unresolved function group has no selected signature to display.
    private string DisplayExpressionTypeName(AstNode expression, TypeExpression type)
        => IsOverloadedFunctionValue(expression) && !selectedFunctionValues.Contains(expression)
            ? "function group" : DisplayTypeName(type);

    private string DisplayTypeName(TypeExpression type, InheritedTypeProtection inherited)
        => NeedsTypeProtectionWrapper(type, inherited)
            ? (type.IsImmutableValue ? "immutable(" : "readonly(") + DisplayTypeNameCore(type, PrintedTypeProtection(type)) + ")"
            : DisplayTypeNameCore(type, inherited);

    private string DisplayTypeNameCore(TypeExpression type, InheritedTypeProtection inherited) => type switch
    {
        NamedTypeExpression n => DisplayName(n.Name) + (n.TypeArguments.Count == 0 ? "" : "<" + string.Join(", ", n.TypeArguments.Select(DisplayTypeName)) + ">"),
        NestedTypeExpression n => DisplayTypeName(n.Parent) + "::" + n.Member + (n.TypeArguments.Count == 0 ? "" : "<" + string.Join(", ", n.TypeArguments.Select(DisplayTypeName)) + ">"),
        ReferenceTypeExpression r => "ref " + DisplayTypeName(r.Inner),
        PointerTypeExpression p => DisplayTypeName(p.Inner, IndirectTypeProtection(inherited)) + "*" + (p.IsNullable ? "?" : ""),
        ManagedTypeExpression p => (p.UsesDefaultCollector || p.Collector == null ? DisplayTypeName(p.Inner, IndirectTypeProtection(inherited)) + "^" : "managed<" + DisplayTypeName(p.Inner, IndirectTypeProtection(inherited)) + ", " + DisplayTypeName(p.Collector) + ">") + (p.IsNullable ? "?" : ""),
        ArrayTypeExpression a => DisplayTypeName(a.ElementType, inherited) + (a.Size.HasValue ? $"[{a.Size}]" : "[]"),
        FunctionPointerTypeExpression f => $"{DisplayTypeName(f.ReturnType)}({string.Join(", ", f.ParameterTypes.Select(DisplayTypeName))}){(f.IsSignature ? "" : f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
        _ => TypeName(type)
    };
}
