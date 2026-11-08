using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public sealed record BinaryFunction(string Namespace, string Name, string Symbol, string Declaration,
    string CppReturn, IReadOnlyList<string> CppParameters);

// Public runtime declarations, separate from implementation source and bodies.
public sealed partial class BinaryInterface(IReadOnlyList<BinaryFunction> functions, IReadOnlyList<BinaryEnum> enums)
{
    public BinaryInterface(IReadOnlyList<BinaryFunction> functions) : this(functions, []) { }
    private static readonly HashSet<string> CppKeywords = ("alignas alignof and and_eq asm auto bitand bitor bool break case catch char char8_t char16_t char32_t class compl concept const consteval constexpr constinit const_cast continue co_await co_return co_yield decltype default delete do double dynamic_cast else enum explicit export extern false float for friend goto if inline int long mutable namespace new noexcept not not_eq nullptr operator or or_eq private protected public register reinterpret_cast requires return short signed sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union unsigned using virtual void volatile wchar_t while xor xor_eq").Split(' ').ToHashSet(StringComparer.Ordinal);
    public IReadOnlyList<BinaryFunction> Functions { get; } = functions;
    public IReadOnlyList<BinaryEnum> Enums { get; } = enums;
    public string HeaderText => string.Concat(Enums.Select(enumeration => EnumDeclarationText(enumeration, cpp: false) + "\n")) +
        string.Concat(Functions.Select(function => InNamespace(function.Namespace, function.Declaration, escaped: true) + "\n"));

    internal static BinaryInterface Create(CompilationUnit unit, TypeChecker checker, IReadOnlyList<string> sourcePaths)
    {
        var paths = sourcePaths.Select(Path.GetFullPath).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var functions = new List<BinaryFunction>();
        var enums = ExportEnums(unit, checker, paths);
        var enumNames = enums.Select(enumeration => enumeration.QualifiedName).ToHashSet(StringComparer.Ordinal);
        Walk(unit.Members);
        foreach (var ns in unit.Namespaces) Walk(ns.Members);
        if (functions.Count == 0 && unit.ForeignUnits.Count == 0)
            throw new TypeCheckException("Binary libraries currently require a public namespace function with a supported runtime signature. Declarations without runtime functions require a source library",
                new SourceFile(sourcePaths.FirstOrDefault() ?? "<library>", "").Span(0));
        return new(functions.OrderBy(function => function.Symbol, StringComparer.Ordinal).ToArray(), enums);

        string CppType(TypeExpression type, bool allowReference = false)
        {
            type = checker.ResolveAlias(type);
            string? mapped = type.WithoutValueProtection() switch {
                PointerTypeExpression pointer => CppType(pointer.Inner) + "*",
                ReferenceTypeExpression reference when allowReference => CppType(reference.Inner) + "&",
                NamedTypeExpression named when checker.GetEnum(named.Name) != null => EnumType(named),
                _ => null
            };
            return mapped == null ? CppLinkage.Type(type, allowReference) : mapped + (type.IsReadOnlyValue ? " const" : "");
        }

        string EnumType(NamedTypeExpression type)
        {
            string name = checker.DisplayTypeName(type.WithoutValueProtection());
            if (!enumNames.Contains(name))
                throw new TypeCheckException("Binary function signature uses an enum that is not exported by this library: " + name, type.Span);
            return "::" + name;
        }

        void Walk(IEnumerable<AstNode> members)
        {
            foreach (var member in members)
            {
                if (member is NamespaceDeclaration ns) { Walk(ns.Members); continue; }
                if (member is not MethodDeclaration method || method.IsGeneric || method.IsPrototype || method.IsForeignImport ||
                    method.Accessibility != TokenKind.Public || checker.IsCompileTimeOnly(method) ||
                    method.Span.DeclarationSourceId is not { } owner || owner.Path.StartsWith('<') || !paths.Contains(Path.GetFullPath(owner.Path))) continue;
                if (method.ExtensionReceiverType != null || method.StringLiteralPrefix != null || method.Name.Contains('$')) continue;
                if (checker.IsEntryPoint(method)) throw new TypeCheckException("A binary library cannot define the program entry point", method.Span);
                string result = checker.DisplayTypeName(checker.ResolveAlias(method.ReturnType));
                string signature = (method.IsStatic ? "static " : "") + result + " @" + method.Name + "(" + string.Join(", ", method.Parameters.Select(parameter =>
                    checker.DisplayTypeName(checker.ResolveAlias(parameter.Type)) + " @" + parameter.Name + checker.ExportParameterDefault(parameter))) + ")" + (method.IsNoThrow ? " nothrow" : "") + ";";
                string scope = checker.GetFunctionNamespace(method).Replace("$", "::");
                functions.Add(new(scope, method.Name, checker.GetNamespaceFunctionSymbol(method), signature,
                    CppType(method.ReturnType), method.Parameters.Select(parameter => CppType(parameter.Type, allowReference: true)).ToArray()));
            }
        }
    }

    public XElement ToXml() => new("GflatInterface", new XAttribute("Version", Enums.Count == 0 ? "1" : "2"), new XAttribute("Target", TargetInfo.Default.Triple),
        Enums.Select(EnumXml),
        Functions.Select(function => new XElement("Function", new XAttribute("Namespace", function.Namespace), new XAttribute("Name", function.Name),
            new XAttribute("Symbol", function.Symbol), new XElement("Declaration", function.Declaration),
            new XElement("CppReturn", function.CppReturn), function.CppParameters.Select(parameter => new XElement("CppParameter", parameter)))));

    public static BinaryInterface Read(string path)
    {
        string content;
        try { content = File.ReadAllText(path); }
        catch (FileNotFoundException) { throw new TypeCheckException("Build the referenced binary library to generate its interface", new SourceFile(path, "").Span(0)); }
        var source = new SourceFile(path, content);
        XElement root;
        try { root = XElement.Parse(source.Text); }
        catch (System.Xml.XmlException error) { throw new TypeCheckException("Invalid library interface: " + error.Message, source.Span(0)); }
        if (root.Name != "GflatInterface" || (string?)root.Attribute("Version") is not ("1" or "2"))
            throw new TypeCheckException("Unsupported library interface format", source.Span(0));
        if ((string?)root.Attribute("Target") != TargetInfo.Default.Triple)
            throw new TypeCheckException("Library interface target does not match " + TargetInfo.Default.Triple, source.Span(0));
        var functions = root.Elements("Function").Select(element => new BinaryFunction(
            Required(element, "Namespace", attribute: true), Required(element, "Name", attribute: true), Required(element, "Symbol", attribute: true),
            Required(element, "Declaration"), Required(element, "CppReturn"), element.Elements("CppParameter").Select(parameter => parameter.Value).ToArray())).ToArray();
        if (functions.Any(function => !Regex.IsMatch(function.Symbol, @"^[A-Za-z_$][A-Za-z0-9_$]*$")))
            throw new TypeCheckException("Invalid native symbol in library interface", source.Span(0));
        var result = new BinaryInterface(functions, root.Elements("Enum").Select(element => ReadEnum(element, source.Span(0))).ToArray());
        if (!File.Exists(path + ".gfh"))
            throw new TypeCheckException("Library declaration header is missing. Rebuild the library", source.Span(0));
        if (File.ReadAllText(path + ".gfh") != result.HeaderText)
            throw new TypeCheckException("Library declaration header differs from its interface metadata. Rebuild the library", source.Span(0));
        return result;

        string Required(XElement element, string name, bool attribute = false) =>
            (attribute ? (string?)element.Attribute(name) : (string?)element.Element(name)) ??
                throw new TypeCheckException("Missing " + name + " in library interface", source.Span(0));
    }

    internal void Bind(CompilationUnit unit)
    {
        var methods = new List<MethodDeclaration>();
        Walk(unit.Members);
        foreach (var ns in unit.Namespaces) Walk(ns.Members);
        if (methods.Count != Functions.Count) throw new TypeCheckException("Library declarations do not match their interface metadata", unit.Span);
        int start = 0;
        var bindings = new Dictionary<int, string>();
        string text = HeaderText;
        foreach (var function in Functions)
        {
            int offset = text.IndexOf(function.Declaration, start, StringComparison.Ordinal);
            bindings.Add(offset, function.Symbol);
            start = offset + function.Declaration.Length;
        }
        foreach (var method in methods)
        {
            if (!method.IsPrototype || !bindings.TryGetValue(method.Span.Start, out var symbol))
                throw new TypeCheckException("Library declarations do not match their interface metadata", method.Span);
            method.NativeSymbol = symbol;
        }
        void Walk(IEnumerable<AstNode> members)
        {
            foreach (var member in members)
                if (member is MethodDeclaration method) methods.Add(method);
                else if (member is NamespaceDeclaration ns) Walk(ns.Members);
        }
    }

    public string CppHeader()
    {
        var text = new StringBuilder("#pragma once\n#include <cstdint>\n\n");
        foreach (var enumeration in Enums) text.AppendLine(EnumDeclarationText(enumeration, cpp: true));
        foreach (var function in Functions)
        {
            string Type(string value) => CppHeaderType(value);
            string parameters = string.Join(", ", function.CppParameters.Select((type, index) => Type(type) + " arg" + index));
            text.Append("extern \"C\" ").Append(Type(function.CppReturn)).Append(' ').Append(function.Symbol).Append('(').Append(parameters).AppendLine(");");
            if (CppKeywords.Contains(function.Name) || function.Namespace.Split("::").Any(CppKeywords.Contains)) continue;
            string body = "inline " + Type(function.CppReturn) + " " + function.Name + "(" + parameters + ") { " +
                (function.CppReturn == "void" ? "" : "return ") + "::" + function.Symbol + "(" +
                string.Join(", ", function.CppParameters.Select((_, index) => "arg" + index)) + "); }";
            text.AppendLine(InNamespace(function.Namespace, body));
        }
        return text.ToString();
    }

    private static string InNamespace(string scope, string declaration, bool escaped = false)
    {
        var parts = scope.Split("::", StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part => "namespace " + (escaped ? "@" : "") + part + " { ")) + declaration + string.Concat(parts.Select(_ => " }"));
    }
}
