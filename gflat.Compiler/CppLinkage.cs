using System.Text;
using System.Text.Json;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

// Clang chooses the native spelling for the configured target ABI.
internal static class CppLinkage
{
    internal static string Type(TypeExpression type)
    {
        string spelling = type.WithoutValueProtection() switch
        {
            NamedTypeExpression { Namespace: null, TypeArguments.Count: 0 } named => named.Name switch
            {
                "void" => "void", "bool" => "bool", "char" => "char", "sbyte" => "signed char", "byte" => "unsigned char",
                "short" => "short", "ushort" => "unsigned short", "int" => "int", "uint" => "unsigned int",
                "long" => "long long", "ulong" => "unsigned long long", "float" => "float", "double" => "double",
                "nint" => "__INTPTR_TYPE__", "nuint" => "__UINTPTR_TYPE__",
                _ => throw Unsupported(type)
            },
            PointerTypeExpression pointer => Type(pointer.Inner) + "*",
            _ => throw Unsupported(type)
        };
        return spelling + (type.IsReadOnlyValue ? " const" : "");
    }

    private static TypeCheckException Unsupported(TypeExpression type) => new(
        "Explicit C++ linkage currently accepts fixed-width scalar types and raw pointers. This signature requires native type interop support", type.Span);

    internal static void Resolve(CompilationUnit unit, TypeChecker checker, CompilationOptions options)
    {
        var functions = new List<ExternDeclaration>();
        Collect(unit.Members);
        foreach (var ns in unit.Namespaces) Collect(ns.Members);
        if (functions.Count == 0) return;
        var source = new StringBuilder();
        var names = new Dictionary<string, ExternDeclaration>();
        foreach (var function in functions)
        {
            string scope = checker.DeclarationNamespace(function) ?? "";
            foreach (string part in scope.Split("::", StringSplitOptions.RemoveEmptyEntries))
                source.Append("namespace ").Append(part).Append(" { ");
            source.Append(Type(function.ReturnType)).Append(' ').Append(function.Name).Append('(')
                .Append(string.Join(", ", function.Parameters.Select(parameter => Type(parameter.Type))))
                .Append(function.IsVariadic ? (function.Parameters.Count == 0 ? "..." : ", ...") : "")
                .Append(')').Append(function.IsNoThrow ? " noexcept" : "").Append(';');
            foreach (string _ in scope.Split("::", StringSplitOptions.RemoveEmptyEntries)) source.Append(" }");
            source.AppendLine();
            names[(scope.Length == 0 ? "" : scope + "::") + function.Name] = function;
        }
        string directory = Path.Combine(Path.GetTempPath(), "gflat_cpp_linkage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "declarations.cpp");
            File.WriteAllText(path, source.ToString());
            string compiler = options.ForeignCompilerResolver?.Invoke() ?? Environment.GetEnvironmentVariable("GFLAT_CLANG") ?? "clang";
            var arguments = options.ForeignCompilerArguments.Concat(new[] { "-x", "c++", "-fsyntax-only", "-Xclang", "-ast-dump=json", path });
            using var ast = JsonDocument.Parse(ForeignImporter.Run(compiler, arguments, functions[0].Span, default));
            Walk(ast.RootElement, "");
            foreach (var function in functions)
                if (function.NativeSymbol == null) throw new TypeCheckException("Clang did not provide a C++ symbol for '" + function.Name + "'", function.Span);
        }
        finally { Directory.Delete(directory, recursive: true); }

        void Collect(IEnumerable<AstNode> members)
        {
            foreach (var member in members)
                if (member is ExternDeclaration { IsCppLinkage: true } function) functions.Add(function);
                else if (member is NamespaceDeclaration ns) Collect(ns.Members);
        }

        void Walk(JsonElement node, string scope)
        {
            string kind = node.TryGetProperty("kind", out var k) ? k.GetString()! : "";
            if (kind == "NamespaceDecl")
            {
                string name = node.GetProperty("name").GetString()!;
                scope += (scope.Length == 0 ? "" : "::") + name;
            }
            else if (kind == "FunctionDecl")
            {
                string name = node.GetProperty("name").GetString()!;
                if (names.TryGetValue((scope.Length == 0 ? "" : scope + "::") + name, out var function))
                    function.NativeSymbol = node.GetProperty("mangledName").GetString();
                return;
            }
            else if (kind != "TranslationUnitDecl") return;
            if (node.TryGetProperty("inner", out var children))
                foreach (var child in children.EnumerateArray()) Walk(child, scope);
        }
    }
}
