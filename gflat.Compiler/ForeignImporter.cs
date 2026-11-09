using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

internal static partial class ForeignImporter
{
    private sealed record CachedDeclaration(string Qualified, string Text, bool IsRecord = false,
        NativeRecordLayout? OpaqueLayout = null, string? Owner = null, CallableRole Role = CallableRole.Member);
    private sealed record InputStamp(long Length, long Ticks, string? Hash);
    private sealed record CachedImport(ForeignTranslationUnit Unit, CachedDeclaration[] Declarations,
        Dictionary<string, string> Symbols, Dictionary<string, InputStamp> Inputs, string[] Dependencies);
    private static readonly Dictionary<string, CachedImport> Cache = new();
    private static readonly object CacheLock = new();

    internal static void Import(CompilationUnit unit, IReadOnlyList<ForeignSource> segments,
        CompilationOptions options, DiagnosticBag diagnostics, Action<string> dependency,
        CancellationToken cancellation, ForeignImportState? state = null, Action<string>? searchDependency = null)
    {
        if (segments.Count == 0) return;
        state ??= new();
        var importedSymbols = state.Symbols;
        var recordDeclarations = state.RecordSignatures;
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
                        .Append(SourcePathLiteral(segment.Span.Source!.Path)).AppendLine();
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
                    foreach (var input in cached.Inputs.Where(input => input.Value.Length == -1)) searchDependency?.Invoke(input.Key);
                    foreach (var symbol in cached.Symbols) importedSymbols.Add(symbol.Key, symbol.Value);
                    foreach (var declaration in cached.Declarations)
                    { cancellation.ThrowIfCancellationRequested(); AddDeclaration(declaration.Qualified, declaration.Text,
                        declaration.IsRecord, declaration.OpaqueLayout, declaration.Owner, declaration.Role); }
                    unit.ForeignSources.AddRange(group);
                    unit.ForeignUnits.Add(cached.Unit);
                    continue;
                }
                var previousSymbols = importedSymbols.Keys.ToHashSet();
                var declarations = new List<CachedDeclaration>();
                var inputs = new Dictionary<string, InputStamp>();
                var headerDependencies = new List<string>();
                var searchDirectories = arguments.Where(Directory.Exists).Select(Path.GetFullPath)
                    .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
                foreach (string searchDirectory in searchDirectories) inputs[searchDirectory] = Stamp(searchDirectory);
                if (File.Exists(compiler)) inputs[compiler] = Stamp(compiler, contents: false);
                string path = Path.Combine(directory, "source." + (group.Key == "cpp" ? "cpp" : "c"));
                string depfile = Path.Combine(directory, "source.d");
                // Capture Clang's Unicode storage types, including flags such as -fshort-wchar.
                // These analysis-only aliases are not added to the emitted native translation unit.
                string builtinTypes = group.Key == "cpp" ? """
                    #ifdef __WCHAR_TYPE__
                    typedef __WCHAR_TYPE__ __gflat_native_wchar_type;
                    #endif
                    #if __cplusplus >= 201103L && defined(__CHAR16_TYPE__)
                    typedef __CHAR16_TYPE__ __gflat_native_char16_type;
                    #endif
                    #if __cplusplus >= 201103L && defined(__CHAR32_TYPE__)
                    typedef __CHAR32_TYPE__ __gflat_native_char32_type;
                    #endif
                    #ifdef __cpp_char8_t
                    typedef unsigned char __gflat_native_char8_type;
                    #endif
                    """ + "\n" : "";
                File.WriteAllText(path, builtinTypes + source);
                var command = new List<string>(arguments);
                command.AddRange(["-x", group.Key == "cpp" ? "c++" : "c", "-fsyntax-only",
                    "-Xclang", "-fdump-record-layouts-complete", "-Xclang", "-fdump-record-layouts-simple", "-Xclang", "-ast-dump=json",
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
                TrackIncludeSearchDirectories(inputs, headerDependencies, searchDirectories);
                foreach (var input in inputs.Where(input => input.Value.Length == -1)) searchDependency?.Invoke(input.Key);
                var layouts = ReadRecordLayouts(ast, out string jsonText);
                using var json = JsonDocument.Parse(jsonText);
                var aliases = new Dictionary<string, string>();
                var records = new Dictionary<string, string>();
                var enums = new Dictionary<string, string>();
                var opaqueRecords = new HashSet<string>();
                CollectAliases(json.RootElement, "");
                var seen = new HashSet<string>();
                string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())))[..24];
                int index = 0;
                var functions = new List<(JsonElement Node, string Scope)>();
                var recordNodes = new List<(JsonElement Node, string Scope)>();
                var enumNodes = new List<(JsonElement Node, string Scope)>();
                Walk(json.RootElement, "");
                var recordDefinitions = recordNodes.Where(item => item.Node.TryGetProperty("completeDefinition", out _))
                    .GroupBy(item => item.Scope.Length == 0 ? Text(item.Node, "name") : item.Scope + "::" + Text(item.Node, "name"))
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                var recordMethods = new Dictionary<string, RecordMethod[]>(StringComparer.Ordinal);
                foreach (var item in enumNodes)
                {
                    if (ImportEnum(item.Node, item.Scope, spelling => Type(spelling, scope: item.Scope)) is not { } enumeration) continue;
                    enums[enumeration.Qualified] = enumeration.Type;
                    AddDeclaration(enumeration.Qualified, enumeration.Declaration, isRecord: true);
                    declarations.Add(new(enumeration.Qualified, enumeration.Declaration, IsRecord: true));
                }
                var importedRecords = recordNodes.GroupBy(item => (item.Scope, Name: Text(item.Node, "name")))
                    .Select(group => group.FirstOrDefault(item => item.Node.TryGetProperty("completeDefinition", out _), group.First()))
                    .Select(item => ImportRecord(item.Node, item.Scope, layouts,
                    spelling => Type(spelling, scope: item.Scope)) ?? ImportOpaqueRecord(item.Node, item.Scope, layouts))
                    .OfType<ImportedRecord>().ToArray();
                foreach (var record in importedRecords)
                {
                    records[record.Qualified] = record.Type;
                    if (record.OpaqueLayout != null) opaqueRecords.Add(record.Type);
                    AddDeclaration(record.Qualified, record.Declaration, isRecord: true, record.OpaqueLayout);
                    declarations.Add(new(record.Qualified, record.Declaration, IsRecord: true, record.OpaqueLayout));
                }
                var recordTypes = records.Values.Where(type => !opaqueRecords.Contains(type)).ToHashSet(StringComparer.Ordinal);
                var signatures = functions.GroupBy(item => item.Scope + "::" + Text(item.Node, "name") + "(" +
                    string.Join(",", item.Node.TryGetProperty("inner", out var children)
                        ? children.EnumerateArray().Where(child => Text(child, "kind") == "ParmVarDecl")
                            .Select(child => Regex.Replace(Type(Text(child.GetProperty("type"), "desugaredQualType", Text(child.GetProperty("type"), "qualType")), scope: item.Scope, allowRecord: true, allowReference: true) ?? "?", "readonly\\(|\\)", ""))
                        : []) + ")");
                foreach (var signature in signatures)
                {
                    var distinct = signature.DistinctBy(item => Text(item.Node.GetProperty("type"), "qualType")).ToArray();
                    if (distinct.Length == 1) Function(distinct[0].Node, distinct[0].Scope);
                }
                if (group.Key == "cpp")
                    foreach (var (node, scope) in recordNodes)
                    {
                        string qualified = scope.Length == 0 ? Text(node, "name") : scope + "::" + Text(node, "name");
                        if (!node.TryGetProperty("completeDefinition", out _) || !records.TryGetValue(qualified, out string? receiver)) continue;
                        foreach (var method in Methods(node, scope))
                        {
                            string key = qualified + "::" + method.Name + "(" + string.Join(",", method.Parameters) + ")";
                            if (importedSymbols.TryGetValue(key, out string? previous))
                            {
                                if (previous == method.Symbol) continue;
                                throw new TypeCheckException("Foreign methods have conflicting imported signatures: " + key, group.First().Span);
                            }
                            importedSymbols.Add(key, method.Symbol);
                            string entry = "__gflat_foreign_" + identity + "_" + index++;
                            string parameters = string.Join(", ", method.Parameters.Select((parameter, n) => parameter + " " + method.ParameterNames[n]));
                            string objectType = method.ReadOnly ? "readonly(" + receiver + ")" : receiver;
                            var bridge = CreateCallBridge(entry, "cpp", scope, method.Result, "", method.Parameters,
                                method.NativeParameters, recordTypes, arguments =>
                                    (method.IsStatic ? "::" + qualified + "::" : "__self->") + method.Name + "(" + string.Join(", ", arguments) + ")",
                                method.IsStatic ? null : objectType,
                                method.IsStatic ? null : (method.ReadOnly ? "const " : "") + Text(node, "tagUsed") + " ::" + qualified,
                                method.ParameterNames, isNoThrow: method.IsNoThrow);
                            bridges.Add(entry, bridge.Native);
                            string declaration = bridge.Extern + "\n" +
                                "public " + (method.IsStatic ? "static " : method.ReadOnly ? "readonly " : "") + method.Result + " @" + method.Name +
                                "(" + parameters + ")" + (method.IsNoThrow ? " nothrow" : "") + " { " + bridge.Body + " }";
                            AddDeclaration(key, declaration, owner: qualified);
                            declarations.Add(new(key, declaration, Owner: qualified));
                        }
                        if (!opaqueRecords.Contains(receiver)) continue;
                        string ownerIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(qualified)))[..24];
                        string constructorFactory = "__gflat_native_construct_" + ownerIdentity;
                        foreach (var constructor in RecordConstructors(node, spelling => Type(spelling, scope: scope, allowRecord: true, allowReference: true)))
                        {
                            string key = qualified + "::.constructor(" + string.Join(",", constructor.Parameters) + ")";
                            if (importedSymbols.ContainsKey(key)) continue;
                            importedSymbols.Add(key, constructor.Symbol);
                            string entry = "__gflat_foreign_" + identity + "_" + index++;
                            var bridge = CreateCallBridge(entry, "cpp", scope, receiver + "*?", "", constructor.Parameters,
                                constructor.NativeParameters, recordTypes, arguments => "new " + Text(node, "tagUsed") + " ::" + qualified +
                                    "(" + string.Join(", ", arguments) + ")", parameterNames: constructor.ParameterNames);
                            bridges.Add(entry, bridge.Native);
                            string declaration = bridge.Extern + "\nprivate " + receiver + "*? " + constructorFactory + "(" +
                                string.Join(", ", constructor.Parameters.Select((parameter, n) => parameter + " " + constructor.ParameterNames[n])) + ") { " + bridge.Body + " }";
                            AddDeclaration(key, declaration, owner: qualified, role: CallableRole.Construction);
                            declarations.Add(new(key, declaration, Owner: qualified, Role: CallableRole.Construction));
                        }
                        string deleteKey = qualified + "::.destruction";
                        if (HasPublicNativeDestructor(node) && !importedSymbols.ContainsKey(deleteKey))
                        {
                            importedSymbols.Add(deleteKey, deleteKey);
                            string factory = "__gflat_native_destroy_" + ownerIdentity;
                            string entry = "__gflat_foreign_" + identity + "_" + index++;
                            var bridge = CreateCallBridge(entry, "cpp", scope, "void", "void", [receiver + "*?"],
                                [Text(node, "tagUsed") + " ::" + qualified + "*"], recordTypes, arguments => "delete " + arguments[0]);
                            bridges.Add(entry, bridge.Native);
                            string declaration = bridge.Extern + "\nprivate void " + factory + "(" + receiver + "*? __arg0) { " + bridge.Body + " }";
                            AddDeclaration(deleteKey, declaration, owner: qualified, role: CallableRole.Destruction);
                            declarations.Add(new(deleteKey, declaration, Owner: qualified, Role: CallableRole.Destruction));
                        }
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

                IEnumerable<RecordMethod> Methods(JsonElement node, string scope)
                {
                    string identity = scope.Length == 0 ? Text(node, "name") : scope + "::" + Text(node, "name");
                    if (recordMethods.TryGetValue(identity, out var cached)) return cached;
                    var declared = RecordMethods(node, spelling => Type(spelling, scope: scope, allowRecord: true),
                        spelling => Type(spelling, scope: scope, allowRecord: true, allowReference: true)).ToArray();
                    if (!node.TryGetProperty("bases", out var bases)) return recordMethods[identity] = declared;
                    var hidden = node.GetProperty("inner").EnumerateArray().Select(child => Text(child, "name").Split("::").Last()).ToHashSet(StringComparer.Ordinal);
                    var inherited = new List<(int Base, RecordMethod Method)>();
                    int index = 0;
                    foreach (var parent in bases.EnumerateArray())
                    {
                        index++;
                        if (Text(parent, "access") != "public") continue;
                        var native = parent.GetProperty("type");
                        string name = Text(native, "desugaredQualType", Text(native, "qualType"));
                        name = Type(name, scope: scope, allowRecord: true, allowOpaqueRecord: true)?.Replace("@", "", StringComparison.Ordinal) ?? name;
                        if (!recordDefinitions.TryGetValue(name, out var definition)) continue;
                        foreach (var method in Methods(definition.Node, definition.Scope))
                            if (!hidden.Contains(method.Name)) inherited.Add((index, method));
                    }
                    return recordMethods[identity] = declared.Concat(inherited.GroupBy(item => item.Method.Name)
                        .Where(group => group.Select(item => item.Base).Distinct().Count() == 1)
                        .SelectMany(group => group.Select(item => item.Method))).ToArray();
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
                    if (kind == "EnumDecl") { enumNodes.Add((node, scope)); return; }
                    if (kind is "RecordDecl" or "CXXRecordDecl") { recordNodes.Add((node, scope)); return; }
                    if (kind == "NamespaceDecl")
                    {
                        string name = Text(node, "name");
                        if (name.Length != 0) scope += (scope.Length == 0 ? "" : "::") + name;
                    }
                    else if (kind is not ("TranslationUnitDecl" or "LinkageSpecDecl")) return;
                    if (node.TryGetProperty("inner", out var children))
                        foreach (var child in children.EnumerateArray()) Walk(child, scope);
                }

                string? Type(string spelling, HashSet<string>? resolving = null, string scope = "", bool allowRecord = false,
                    bool allowOpaqueRecord = false, bool allowReference = false)
                {
                    spelling = spelling.Trim();
                    if (spelling.EndsWith('&'))
                    {
                        if (!allowReference || spelling.EndsWith("&&", StringComparison.Ordinal)) return null;
                        string? referred = Type(spelling[..^1], resolving, scope, allowRecord: true, allowOpaqueRecord: true);
                        return referred == null || referred == "void" ? null : "ref " + referred;
                    }
                    var callback = Regex.Match(spelling, @"^(?<result>.+?)\s*\(\s*\*\s*(?<constant>const\s*)?\)\s*\((?<parameters>.*)\)$");
                    if (callback.Success)
                    {
                        string? result = Type(callback.Groups["result"].Value, resolving, scope);
                        var parameters = SplitParameters(callback.Groups["parameters"].Value);
                        if (result == null || parameters == null) return null;
                        var types = parameters.Select(parameter => Type(parameter, resolving, scope)).ToArray();
                        if (types.Any(type => type == null)) return null;
                        string mapped = result + "(" + string.Join(", ", types) + ")*?";
                        return callback.Groups["constant"].Success ? "readonly(" + mapped + ")" : mapped;
                    }
                    if (spelling.EndsWith("const", StringComparison.Ordinal) && spelling.Length > 5 &&
                        (char.IsWhiteSpace(spelling[^6]) || spelling[^6] == '*'))
                    {
                        string? inner = Type(spelling[..^5], resolving, scope, allowRecord, allowOpaqueRecord);
                        return inner == null ? null : "readonly(" + inner + ")";
                    }
                    if (spelling.EndsWith('*'))
                    {
                        string? inner = Type(spelling[..^1], resolving, scope, allowRecord: true, allowOpaqueRecord: true);
                        return inner == null ? null : inner + "*?";
                    }
                    bool readOnly = spelling.StartsWith("const ", StringComparison.Ordinal) || spelling.EndsWith(" const", StringComparison.Ordinal);
                    if (readOnly) spelling = spelling.StartsWith("const ", StringComparison.Ordinal) ? spelling[6..] : spelling[..^6];
                    if (spelling.StartsWith("struct ", StringComparison.Ordinal)) spelling = spelling[7..];
                    else if (spelling.StartsWith("class ", StringComparison.Ordinal)) spelling = spelling[6..];
                    else if (spelling.StartsWith("union ", StringComparison.Ordinal)) spelling = spelling[6..];
                    else if (spelling.StartsWith("enum ", StringComparison.Ordinal)) spelling = spelling[5..];
                    string? primitive = spelling.Trim() switch
                    {
                        "void" => "void", "bool" or "_Bool" => "bool", "char" => "char",
                        "wchar_t" when group.Key == "cpp" && !aliases.ContainsKey("wchar_t") => Type("::__gflat_native_wchar_type", resolving),
                        "char8_t" when aliases.ContainsKey("__gflat_native_char8_type") => Type("::__gflat_native_char8_type", resolving),
                        "char16_t" when aliases.ContainsKey("__gflat_native_char16_type") => Type("::__gflat_native_char16_type", resolving),
                        "char32_t" when aliases.ContainsKey("__gflat_native_char32_type") => Type("::__gflat_native_char32_type", resolving),
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
                        if (aliases.ContainsKey(parent + "::" + alias) || records.ContainsKey(parent + "::" + alias) || enums.ContainsKey(parent + "::" + alias))
                        { alias = parent + "::" + alias; break; }
                        int separator = parent.LastIndexOf("::", StringComparison.Ordinal);
                        parent = separator < 0 ? "" : parent[..separator];
                    }
                    if (primitive == null && aliases.TryGetValue(alias, out string? target))
                    {
                        resolving ??= [];
                        if (!resolving.Add(alias)) return null;
                        int separator = alias.LastIndexOf("::", StringComparison.Ordinal);
                        primitive = Type(target, resolving, separator < 0 ? "" : alias[..separator], allowRecord, allowOpaqueRecord, allowReference);
                        resolving.Remove(alias);
                    }
                    if (primitive == null && allowRecord && records.TryGetValue(alias, out var record) &&
                        (allowOpaqueRecord || !opaqueRecords.Contains(record))) primitive = record;
                    if (primitive == null && enums.TryGetValue(alias, out var enumeration)) primitive = enumeration;
                    return primitive == null ? null : readOnly && primitive != "void" ? "readonly(" + primitive + ")" : primitive;
                }

                void Function(JsonElement function, string scope)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string name = Text(function, "name");
                    if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") ||
                        function.TryGetProperty("isImplicit", out _) || function.TryGetProperty("explicitlyDeleted", out _)) return;
                    string signature = Text(function.GetProperty("type"), "qualType");
                    if (FunctionShape(signature) is not { } shape) return;
                    string nativeResult = shape.Result;
                    var contract = FunctionContract(function, shape.Qualifiers);
                    string? result = Type(nativeResult, scope: scope, allowRecord: true);
                    if (result == null) return;
                    var parameters = function.TryGetProperty("inner", out var body)
                        ? body.EnumerateArray().Where(child => Text(child, "kind") == "ParmVarDecl").ToArray() : [];
                    var parameterTypes = parameters.Select(parameter => parameter.GetProperty("type"))
                        .Select(type => Text(type, "desugaredQualType", Text(type, "qualType"))).ToArray();
                    var types = parameterTypes.Select(type => Type(type, scope: scope, allowRecord: true, allowReference: true)).ToArray();
                    var parameterNames = ParameterNames(parameters);
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
                    bool variadic = function.TryGetProperty("variadic", out var flag) && flag.GetBoolean();
                    if (variadic && (RecordValueType(result, recordTypes) != null ||
                        types.Any(type => RecordValueType(type!, recordTypes) != null))) return;
                    importedSymbols.Add(importKey, symbol);
                    string declaration;
                    if (variadic)
                    {
                        // A variadic bridge cannot forward its unnamed arguments.
                        if (group.Key == "cpp" && Text(function, "mangledName") != name) return;
                        declaration = "extern \"C\" " + result + " @" + name + "(" + string.Join(", ", types.Select((type, n) => type + " " + parameterNames[n])) +
                            (types.Length == 0 ? "" : ", ") + "...)" + (contract.IsNoThrow ? " nothrow" : "") + ";";
                    }
                    else
                    {
                        string entry = "__gflat_foreign_" + identity + "_" + index++;
                        string parametersText = string.Join(", ", types.Select((type, n) => type + " " + parameterNames[n]));
                        var bridge = CreateCallBridge(entry, group.Key, scope, result, nativeResult,
                            types.Select(type => type!).ToArray(), parameterTypes, recordTypes,
                            arguments => "(" + (group.Key == "cpp" ? "::" : "") + qualified + ")(" + string.Join(", ", arguments) + ")",
                            parameterNames: parameterNames, isNoThrow: contract.IsNoThrow);
                        bridges.Add(entry, bridge.Native);
                        declaration = bridge.Extern + "\npublic " + result + " @" + name + "(" + parametersText + ")" +
                            (contract.IsNoThrow ? " nothrow" : "") + " { " + bridge.Body + " }";
                    }
                    if (scope.Length != 0) declaration = "namespace " + string.Join("::", scope.Split("::").Select(part => "@" + part)) + " { " + declaration + " }";
                    AddDeclaration(qualified, declaration);
                    declarations.Add(new(qualified, declaration));
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }

        void AddDeclaration(string qualified, string declaration, bool isRecord = false,
            NativeRecordLayout? opaqueLayout = null, string? owner = null, CallableRole role = CallableRole.Member)
        {
            if (isRecord)
            {
                string signature = declaration + "\n" + opaqueLayout;
                if (recordDeclarations.TryGetValue(qualified, out string? previous))
                {
                    if (previous != signature)
                        throw new TypeCheckException("Foreign records have conflicting imported definitions: " + qualified, segments[0].Span);
                    return;
                }
                recordDeclarations.Add(qualified, signature);
            }
            var imported = Parser.Parse(Lexer.Tokenize(new SourceFile("<foreign:" + qualified + ">", declaration)), diagnostics);
            Mark(imported.Members);
            foreach (var ns in imported.Namespaces) Mark(ns.Members);
            foreach (var member in imported.Members)
            {
                if (owner != null && member is MethodDeclaration method)
                {
                    if (role == CallableRole.Member) { state.Records[owner].Members.Add(member); continue; }
                    if (role == CallableRole.Construction) state.Records[owner].NativeConstructorFactory = method.Name;
                    else state.Records[owner].NativeDeleteFactory = method.Name;
                }
                unit.Members.Add(member);
            }
            unit.Namespaces.AddRange(imported.Namespaces);
            void Mark(IEnumerable<AstNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node is MethodDeclaration method) method.IsForeignImport = true;
                    else if (node is NamespaceDeclaration ns) Mark(ns.Members);
                    else if (node is ExtensionDeclaration extension) Mark(extension.Members);
                    else if (node is StructDeclaration structure)
                    {
                        structure.IsForeignImport = isRecord;
                        structure.NativeOpaqueLayout = opaqueLayout;
                        if (isRecord) state.Records[qualified] = structure;
                    }
                }
            }
        }
    }

    private static (string Result, string Qualifiers)? FunctionShape(string signature)
    {
        int opening = signature.IndexOf('(');
        if (opening < 0) return null;
        var declarator = signature.AsSpan(opening + 1).TrimStart();
        if (declarator.IsEmpty || declarator[0] is not ('*' or '&'))
        {
            int closing = Closing(opening);
            if (closing < 0) return null;
            string result = signature[..opening].Trim();
            if (result != "auto") return (result, signature[(closing + 1)..].Trim());

            // Trailing results follow receiver/noexcept qualifiers. Skip their
            // parenthesized expressions so an inner member access is not the arrow.
            for (int index = closing + 1; index + 1 < signature.Length; index++)
            {
                if (signature[index] == '(')
                {
                    index = Closing(index);
                    if (index < 0) return null;
                }
                else if (signature[index] == '-' && signature[index + 1] == '>')
                    return (signature[(index + 2)..].Trim(), signature[(closing + 1)..index].Trim());
            }
            return null;
        }

        // Clang spells a callback result as R (*(function parameters))(callback parameters).
        // Receiver qualifiers occur inside the pointer declarator, after the
        // function parameters. Keep them separate from the returned pointer type.
        int parameters = signature.IndexOf('(', opening + 1);
        if (parameters < 0 || signature[(opening + 1)..parameters].Trim() != "*") return null;
        int endParameters = Closing(parameters), endDeclarator = Closing(opening);
        if (endParameters < 0 || endDeclarator <= endParameters) return null;
        return (signature.Remove(parameters, endDeclarator - parameters).Trim(),
            signature[(endParameters + 1)..endDeclarator].Trim());

        int Closing(int start)
        {
            int depth = 0;
            for (int index = start; index < signature.Length; index++)
            {
                if (signature[index] == '(') depth++;
                else if (signature[index] == ')' && --depth == 0) return index;
            }
            return -1;
        }
    }

    private static string[]? SplitParameters(string text)
    {
        if (text.Trim() is "" or "void") return [];
        var parameters = new List<string>();
        int depth = 0, start = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '(') depth++;
            else if (text[index] == ')' && --depth < 0) return null;
            else if (text[index] == ',' && depth == 0)
            {
                parameters.Add(text[start..index].Trim());
                start = index + 1;
            }
        }
        if (depth != 0) return null;
        parameters.Add(text[start..].Trim());
        return parameters.ToArray();
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

    internal static string Run(string compiler, IEnumerable<string> arguments, SourceSpan span, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(compiler) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
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
