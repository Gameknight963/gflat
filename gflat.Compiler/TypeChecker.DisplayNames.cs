using gflat.ast;
using System.Text.RegularExpressions;

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
            ManagedTypeExpression p => DisplayTypeName(p.Inner, inheritedReadOnly) + "^" + (p.IsNullable ? "?" : ""),
            ArrayTypeExpression a => DisplayTypeName(a.ElementType, inheritedReadOnly) + (a.Size.HasValue ? $"[{a.Size}]" : "[]"),
            FunctionPointerTypeExpression f => $"{DisplayTypeName(f.ReturnType)}({string.Join(", ", f.ParameterTypes.Select(DisplayTypeName))}){(f.IsSignature ? "" : f.IsManaged ? "^" : "*")}{(f.IsNullable ? "?" : "")}",
            _ => TypeName(type)
        };
    }

    public string DisplayDiagnostic(string message) => Regex.Replace(message,
        @"[\p{L}_$][\p{L}\p{N}_$]*(?:(?:::|\.)[\p{L}_$][\p{L}\p{N}_$]*)*",
        match => _displayNames.ContainsKey(match.Value) || _classes.ContainsKey(match.Value) || _structs.ContainsKey(match.Value) || _interfaces.ContainsKey(match.Value) || _enums.ContainsKey(match.Value)
            ? DisplayName(match.Value) : match.Value);
}
