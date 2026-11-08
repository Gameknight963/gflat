using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

internal static class ForeignImporter
{
    private sealed record CachedDeclaration(string Qualified, string Text);
    private sealed record InputStamp(long Length, long Ticks, string? Hash);
    private sealed record CachedImport(ForeignTranslationUnit Unit, CachedDeclaration[] Declarations,
        Dictionary<string, string> Symbols, Dictionary<string, InputStamp> Inputs, string[] Dependencies);
    private static readonly Dictionary<string, CachedImport> Cache = new();
    private static readonly object CacheLock = new();

    internal static void Import(CompilationUnit unit, IReadOnlyList<ForeignSource> segments,
        CompilationOptions options, DiagnosticBag diagnostics, Action<string> dependency,
        CancellationToken cancellation, Dictionary<string, string>? importedSymbols = null)
    {
        if (segments.Count == 0) return;
        importedSymbols ??= new();
        string directory = Path.Combine(Path.GetTempPath(), "gflat_foreign_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var group in segments.GroupBy(segment => segment.Language))
            {
                cancellation.ThrowIfCancellationRequested();
                var source = new StringBuilder();
                var bridges = new Dictionary<string, string>();
                var arguments = new List<string>(options.ForeignCompilerArguments);
                foreach (string include in options.IncludeDirectories) arguments.AddRange(["-I", Path.GetFullPath(include)]);
                foreach (var define in options.Defines) arguments.Add("-D" + define.Key + "=" + define.Value);
                foreach (string parent in group.Select(segment => segment.Span.Source!.Path)
                    .Where(path => !path.StartsWith('<')).Select(path => Path.GetDirectoryName(Path.GetFullPath(path))!).Distinct())
                    arguments.AddRange(["-iquote", parent]);
                foreach (var segment in group)
                {
                    source.Append("#line ").Append(segment.Span.Line).Append(' ')
                        .Append(JsonSerializer.Serialize(segment.Span.Source!.Path.Replace('\\', '/'))).AppendLine();
                    source.Append(segment.Span.Source.Text.AsSpan(segment.Span.Start, segment.Span.Length)).AppendLine();
                }
                string compiler = options.ForeignCompilerResolver?.Invoke() ?? Environment.GetEnvironmentVariable("GFLAT_CLANG") ?? "clang";
                string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                    Compiler = compiler, Arguments = arguments, Source = source.ToString(), Language = group.Key,
                    Symbols = importedSymbols.OrderBy(pair => pair.Key).ToArray(),
                    Environment = new[] { "PATH", "INCLUDE", "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "SDKROOT" }
                        .Select(Environment.GetEnvironmentVariable).ToArray()
                }))));
                CachedImport? cached;
                lock (CacheLock) Cache.TryGetValue(cacheKey, out cached);
                if (cached != null && cached.Inputs.All(input => input.Value.Length >= -1 && Stamp(input.Key, input.Value.Hash != null) == input.Value))
                {
                    foreach (string file in cached.Dependencies) dependency(file);
                    foreach (var symbol in cached.Symbols) importedSymbols.Add(symbol.Key, symbol.Value);
                    foreach (var declaration in cached.Declarations) { cancellation.ThrowIfCancellationRequested(); AddDeclaration(declaration.Qualified, declaration.Text); }
                    unit.ForeignSources.AddRange(group);
                    unit.ForeignUnits.Add(cached.Unit);
                    continue;
                }
                var previousSymbols = importedSymbols.Keys.ToHashSet();
                var declarations = new List<CachedDeclaration>();
                var inputs = new Dictionary<string, InputStamp>();
                var headerDependencies = new List<string>();
                foreach (string argument in arguments.Where(Directory.Exists)) inputs[argument] = Stamp(argument);
                if (File.Exists(compiler)) inputs[compiler] = Stamp(compiler, contents: false);
                string path = Path.Combine(directory, "source." + (group.Key == "cpp" ? "cpp" : "c"));
                string depfile = Path.Combine(directory, "source.d");
                File.WriteAllText(path, source.ToString());
                var command = new List<string>(arguments);
                command.AddRange(["-x", group.Key == "cpp" ? "c++" : "c", "-fsyntax-only", "-Xclang", "-ast-dump=json",
                    "-MD", "-MF", depfile, path]);
                string ast;
                try { ast = Run(compiler, command, group.First().Span, cancellation); }
                finally
                {
                    if (File.Exists(depfile))
                        foreach (string file in ReadDependencies(File.ReadAllText(depfile)))
                            if (!string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                            { dependency(file); headerDependencies.Add(file); inputs[file] = Stamp(file); }
                }
                using var json = JsonDocument.Parse(ast);
                var aliases = new Dictionary<string, string>();
                CollectAliases(json.RootElement, "");
                var seen = new HashSet<string>();
                string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())))[..24];
                int index = 0;
                var functions = new List<(JsonElement Node, string Scope)>();
                Walk(json.RootElement, "");
                var signatures = functions.GroupBy(item => item.Scope + "::" + Text(item.Node, "name") + "(" +
                    string.Join(",", item.Node.TryGetProperty("inner", out var children)
                        ? children.EnumerateArray().Where(child => Text(child, "kind") == "ParmVarDecl")
                            .Select(child => Regex.Replace(Type(Text(child.GetProperty("type"), "desugaredQualType", Text(child.GetProperty("type"), "qualType")), scope: item.Scope) ?? "?", "readonly\\(|\\)", ""))
                        : []) + ")");
                foreach (var signature in signatures)
                {
                    var distinct = signature.DistinctBy(item => Text(item.Node.GetProperty("type"), "qualType")).ToArray();
                    if (distinct.Length == 1) Function(distinct[0].Node, distinct[0].Scope);
                }
                unit.ForeignSources.AddRange(group);
                var nativeUnit = new ForeignTranslationUnit(group.Key, source.ToString(), arguments.AsReadOnly())
                { Bridges = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(bridges) };
                unit.ForeignUnits.Add(nativeUnit);
                lock (CacheLock)
                {
                    if (Cache.Count >= 8) Cache.Remove(Cache.Keys.First());
                    Cache[cacheKey] = new(nativeUnit, declarations.ToArray(),
                        importedSymbols.Where(pair => !previousSymbols.Contains(pair.Key)).ToDictionary(), inputs, headerDependencies.ToArray());
                }

                void CollectAliases(JsonElement node, string scope)
                {
                    string kind = Text(node, "kind");
                    if (kind is "TypedefDecl" or "TypeAliasDecl" && node.TryGetProperty("type", out var type))
                        aliases.TryAdd((scope.Length == 0 ? "" : scope + "::") + Text(node, "name"), Text(type, "desugaredQualType", Text(type, "qualType")));
                    if (kind == "NamespaceDecl" && Text(node, "name") is { Length: > 0 } name)
                        scope += (scope.Length == 0 ? "" : "::") + name;
                    else if (kind is not ("TranslationUnitDecl" or "LinkageSpecDecl" or "NamespaceDecl")) return;
                    if (node.TryGetProperty("inner", out var children))
                        foreach (var child in children.EnumerateArray()) CollectAliases(child, scope);
                }

                void Walk(JsonElement node, string scope)
                {
                    string kind = Text(node, "kind");
                    if (kind == "FunctionDecl") { functions.Add((node, scope)); return; }
                    if (kind == "NamespaceDecl")
                    {
                        string name = Text(node, "name");
                        if (name.Length != 0) scope += (scope.Length == 0 ? "" : "::") + name;
                    }
                    else if (kind is not ("TranslationUnitDecl" or "LinkageSpecDecl")) return;
                    if (node.TryGetProperty("inner", out var children))
                        foreach (var child in children.EnumerateArray()) Walk(child, scope);
                }

                string? Type(string spelling, HashSet<string>? resolving = null, string scope = "")
                {
                    spelling = spelling.Trim();
                    if (spelling.EndsWith("const", StringComparison.Ordinal) && spelling.Length > 5 &&
                        (char.IsWhiteSpace(spelling[^6]) || spelling[^6] == '*'))
                    {
                        string? inner = Type(spelling[..^5], resolving, scope);
                        return inner == null ? null : "readonly(" + inner + ")";
                    }
                    if (spelling.EndsWith('*'))
                    {
                        string? inner = Type(spelling[..^1], resolving, scope);
                        return inner == null ? null : inner + "*?";
                    }
                    bool readOnly = spelling.StartsWith("const ", StringComparison.Ordinal) || spelling.EndsWith(" const", StringComparison.Ordinal);
                    if (readOnly) spelling = spelling.StartsWith("const ", StringComparison.Ordinal) ? spelling[6..] : spelling[..^6];
                    string? primitive = spelling.Trim() switch
                    {
                        "void" => "void", "bool" or "_Bool" => "bool", "char" => "char",
                        "signed char" => "sbyte", "unsigned char" => "byte",
                        "short" or "short int" or "signed short" => "short", "unsigned short" or "unsigned short int" => "ushort",
                        "int" or "signed int" or "signed" => "int", "unsigned" or "unsigned int" => "uint",
                        "long" or "long int" => OperatingSystem.IsWindows() ? "int" : "long",
                        "unsigned long" or "unsigned long int" => OperatingSystem.IsWindows() ? "uint" : "ulong",
                        "long long" or "long long int" => "long", "unsigned long long" or "unsigned long long int" => "ulong",
                        "float" => "float", "double" => "double", _ => null
                    };
                    string alias = spelling.StartsWith("::", StringComparison.Ordinal) ? spelling[2..] : spelling;
                    for (string parent = scope; parent.Length > 0;)
                    {
                        if (aliases.ContainsKey(parent + "::" + alias)) { alias = parent + "::" + alias; break; }
                        int separator = parent.LastIndexOf("::", StringComparison.Ordinal);
                        parent = separator < 0 ? "" : parent[..separator];
                    }
                    if (primitive == null && aliases.TryGetValue(alias, out string? target))
                    {
                        resolving ??= [];
                        if (!resolving.Add(alias)) return null;
                        int separator = alias.LastIndexOf("::", StringComparison.Ordinal);
                        primitive = Type(target, resolving, separator < 0 ? "" : alias[..separator]);
                        resolving.Remove(alias);
                    }
                    return primitive == null ? null : readOnly && primitive != "void" ? "readonly(" + primitive + ")" : primitive;
                }

                void Function(JsonElement function, string scope)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string name = Text(function, "name");
                    if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") ||
                        function.TryGetProperty("isImplicit", out _) || function.TryGetProperty("explicitlyDeleted", out _)) return;
                    string signature = Text(function.GetProperty("type"), "qualType");
                    int opening = signature.IndexOf('(');
                    if (opening < 0) return;
                    string? result = Type(signature[..opening], scope: scope);
                    if (result == null) return;
                    var parameters = function.TryGetProperty("inner", out var body)
                        ? body.EnumerateArray().Where(child => Text(child, "kind") == "ParmVarDecl").ToArray() : [];
                    var parameterTypes = parameters.Select(parameter => parameter.GetProperty("type"))
                        .Select(type => Text(type, "desugaredQualType", Text(type, "qualType"))).ToArray();
                    var types = parameterTypes.Select(type => Type(type, scope: scope)).ToArray();
                    if (types.Any(type => type == null)) return;
                    string qualified = scope.Length == 0 ? name : scope + "::" + name;
                    if (!seen.Add(qualified + signature)) return;
                    string importKey = qualified + "(" + string.Join(",", types.Select(type => Regex.Replace(type!, "readonly\\(|\\)", ""))) + ")";
                    string symbol = Text(function, "mangledName", qualified);
                    if (importedSymbols.TryGetValue(importKey, out string? previous))
                    {
                        if (previous == symbol) return;
                        throw new TypeCheckException("Foreign functions have conflicting imported signatures: " + qualified, group.First().Span);
                    }
                    importedSymbols.Add(importKey, symbol);
                    bool variadic = function.TryGetProperty("variadic", out var flag) && flag.GetBoolean();
                    string declaration;
                    if (variadic)
                    {
                        // A variadic bridge cannot forward its unnamed arguments.
                        if (group.Key == "cpp" && Text(function, "mangledName") != name) return;
                        declaration = "extern \"C\" " + result + " @" + name + "(" + string.Join(", ", types.Select((type, n) => type + " __arg" + n)) +
                            (types.Length == 0 ? "" : ", ") + "...);";
                    }
                    else
                    {
                        string entry = "__gflat_foreign_" + identity + "_" + index++;
                        string parametersText = string.Join(", ", types.Select((type, n) => type + " __arg" + n));
                        string values = string.Join(", ", types.Select((_, n) => "__arg" + n));
                        var native = new StringBuilder();
                        if (group.Key == "cpp") native.Append("extern \"C\" ");
                        native.Append(group.Key == "cpp" ? "auto" : signature[..opening].Trim()).Append(' ').Append(entry).Append('(')
                            .Append(string.Join(", ", parameterTypes.Select((type, n) => type + " __arg" + n))).Append(')');
                        if (group.Key == "cpp") native.Append(" -> decltype((::").Append(qualified).Append(")(").Append(values).Append("))");
                        native.Append(" { ");
                        if (result != "void") native.Append("return ");
                        native.Append('(').Append(group.Key == "cpp" ? "::" : "").Append(qualified).Append(")(").Append(values).AppendLine("); }");
                        bridges.Add(entry, native.ToString());
                        declaration = "private extern \"C\" " + result + " " + entry + "(" + parametersText + ");\n" +
                            "public " + result + " @" + name + "(" + parametersText + ") { " +
                            (result == "void" ? "" : "return ") + entry + "(" + values + "); }";
                    }
                    if (scope.Length != 0) declaration = "namespace " + string.Join("::", scope.Split("::").Select(part => "@" + part)) + " { " + declaration + " }";
                    AddDeclaration(qualified, declaration);
                    declarations.Add(new(qualified, declaration));
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        void AddDeclaration(string qualified, string declaration)
        {
            var imported = Parser.Parse(Lexer.Tokenize(new SourceFile("<foreign:" + qualified + ">", declaration)), diagnostics);
            Mark(imported.Members);
            foreach (var ns in imported.Namespaces) Mark(ns.Members);
            unit.Members.AddRange(imported.Members);
            unit.Namespaces.AddRange(imported.Namespaces);
            static void Mark(IEnumerable<AstNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node is MethodDeclaration method) method.IsForeignImport = true;
                    else if (node is NamespaceDeclaration ns) Mark(ns.Members);
                }
            }
        }
    }

    private static InputStamp Stamp(string path, bool contents = true)
    {
        try
        {
            if (Directory.Exists(path)) return new(-1, Directory.GetLastWriteTimeUtc(path).Ticks, null);
            var file = new FileInfo(path);
            if (!file.Exists) return new(-2, 0, null);
            if (!contents) return new(file.Length, file.LastWriteTimeUtc.Ticks, null);
            using var stream = File.OpenRead(path);
            return new(file.Length, file.LastWriteTimeUtc.Ticks, Convert.ToHexString(SHA256.HashData(stream)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(-2, 0, null); }
    }

    private static string Text(JsonElement node, string name, string fallback = "")
        => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;

    private static string Run(string compiler, IEnumerable<string> arguments, SourceSpan span, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(compiler) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception error) { throw new TypeCheckException("Cannot start Clang for #lang: " + error.Message, span); }
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            cancellation.ThrowIfCancellationRequested();
            throw new TypeCheckException("Clang timed out while checking #lang source", span);
        }
        string stderr = errors.GetAwaiter().GetResult();
        string stdout = output.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new TypeCheckException("Foreign source compilation failed:\n" + stderr, span);
        return stdout;
    }

    private static IEnumerable<string> ReadDependencies(string text)
    {
        text = text.Replace("\\\r\n", "").Replace("\\\n", "");
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0) colon = text.IndexOf(":\n", StringComparison.Ordinal);
        if (colon < 0) yield break;
        foreach (Match match in Regex.Matches(text[(colon + 1)..], @"(?:\\.|[^\s])+"))
            yield return Regex.Replace(match.Value, @"\\([ #\\])", "$1");
    }
}
