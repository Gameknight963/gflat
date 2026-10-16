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

    public string DisplayTypeName(TypeExpression type) => DisplayTypeName(type, false);

    private string DisplayTypeName(TypeExpression type, bool inheritedReadOnly)
    {
        if (type.IsReadOnlyValue && !inheritedReadOnly)
            return "readonly(" + DisplayTypeName(type.WithReadOnlyValue(false), true) + ")";
        return type switch
        {
            NamedTypeExpression n => DisplayName(n.Name) + (n.TypeArguments.Count == 0 ? "" : "<" + string.Join(", ", n.TypeArguments.Select(DisplayTypeName)) + ">"),
            NestedTypeExpression n => DisplayTypeName(n.Parent) + "::" + n.Member + (n.TypeArguments.Count == 0 ? "" : "<" + string.Join(", ", n.TypeArguments.Select(DisplayTypeName)) + ">"),
            ReferenceTypeExpression r => "ref " + DisplayTypeName(r.Inner),
            PointerTypeExpression p => DisplayTypeName(p.Inner, inheritedReadOnly) + "*" + (p.IsNullable ? "?" : ""),
            ManagedTypeExpression p => (p.UsesDefaultCollector || p.Collector == null ? DisplayTypeName(p.Inner, inheritedReadOnly) + "^" : "managed<" + DisplayTypeName(p.Inner, inheritedReadOnly) + ", " + DisplayTypeName(p.Collector) + ">") + (p.IsNullable ? "?" : ""),
            ArrayTypeExpression a => DisplayTypeName(a.ElementType, inheritedReadOnly) + (a.Size.HasValue ? $"[{a.Size}]" : "[]"),
            FunctionPointerTypeExpression f => $"{DisplayTypeName(f.ReturnType)}({string.Join(", ", f.ParameterTypes.Select(DisplayTypeName))}){(f.IsSignature ? "" : f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => TypeName(type)
        };
    }

}
