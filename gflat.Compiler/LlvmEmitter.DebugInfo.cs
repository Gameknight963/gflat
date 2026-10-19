using System.Text;
using System.Security.Cryptography;
using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly bool _debugSymbols;
    private readonly List<string> _debugNodes = new();
    private readonly Dictionary<string, string> _debugFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Scope, int Line, int Column), string> _debugLocations = new();
    private string? _debugUnit;
    private readonly Dictionary<string, string> _debugTypes = new();

    private sealed class DebugScope(Action restore) : IDisposable { public void Dispose() => restore(); }

    private IDisposable EnterDebugBlock(BlockStatement node)
    {
        if (!_debugSymbols || !_functions.TryPeek(out var function) || function.DebugScope == null)
            return new DebugScope(() => { });
        string previous = function.DebugScope;
        string? location = function.DebugLocation;
        function.DebugScope = DebugNode($"distinct !DILexicalBlock(scope: {previous}, file: {DebugFile(node)}, line: {Math.Max(1, node.Line)}, column: {Math.Max(1, node.Span.Column)})");
        return new DebugScope(() => { function.DebugScope = previous; function.DebugLocation = location; });
    }

    private string DebugType(TypeExpression source)
    {
        TypeExpression type = _typeChecker.ResolveAlias(source);
        string key = _typeChecker.DisplayTypeName(type);
        if (_debugTypes.TryGetValue(key, out string? existing)) return existing;
        if (type is NamedTypeExpression { Name: "void" }) return "null";
        string id = DebugNode("!{}");
        _debugTypes[key] = id; // Publish before visiting fields of recursive types.
        string value;
        if (type.IsReadOnlyValue)
            value = $"!DIDerivedType(tag: DW_TAG_const_type, baseType: {DebugType(type.WithReadOnlyValue(false))})";
        else if (type is PointerTypeExpression pointer)
        {
            if (_typeChecker.ResolveAlias(pointer.Inner) is NamedTypeExpression inner && _typeChecker.IsInterface(inner))
            {
                string raw = DebugType(new PointerTypeExpression(new NamedTypeExpression("void", null, 0), false, 0));
                string instance = DebugNode($"!DIDerivedType(tag: DW_TAG_member, name: \"__object\", scope: {id}, baseType: {raw}, size: {TargetInfo.Default.PointerBits}, offset: 0)");
                string table = DebugNode($"!DIDerivedType(tag: DW_TAG_member, name: \"__vtable\", scope: {id}, baseType: {raw}, size: {TargetInfo.Default.PointerBits}, offset: {TargetInfo.Default.PointerBits})");
                value = $"distinct !DICompositeType(tag: DW_TAG_structure_type, name: {DebugString(key)}, size: {TargetInfo.Default.PointerBits * 2}, elements: !{{{instance}, {table}}})";
            }
            else value = $"!DIDerivedType(tag: DW_TAG_pointer_type, baseType: {DebugType(pointer.Inner)}, size: {TargetInfo.Default.PointerBits})";
        }
        else if (type is ManagedTypeExpression managed)
            value = $"!DIDerivedType(tag: DW_TAG_pointer_type, name: {DebugString(key)}, baseType: {DebugType(managed.Inner)}, size: {TargetInfo.Default.PointerBits})";
        else if (type is ReferenceTypeExpression reference)
            value = $"!DIDerivedType(tag: DW_TAG_reference_type, baseType: {DebugType(reference.Inner)}, size: {TargetInfo.Default.PointerBits})";
        else if (type is ArrayTypeExpression { Size: null } unsized)
            value = $"!DIDerivedType(tag: DW_TAG_pointer_type, baseType: {DebugType(unsized.ElementType)}, size: {TargetInfo.Default.PointerBits})";
        else if (type is ArrayTypeExpression array)
        {
            string range = DebugNode($"!DISubrange(count: {array.Size ?? 0})");
            value = $"!DICompositeType(tag: DW_TAG_array_type, baseType: {DebugType(array.ElementType)}, size: {_typeChecker.GetTypeSize(type) * 8L}, elements: !{{{range}}})";
        }
        else if (type is FunctionPointerTypeExpression function)
        {
            string signature = $"!DISubroutineType(types: !{{{string.Join(", ", new[] { DebugType(function.ReturnType) }.Concat(function.ParameterTypes.Select(DebugType)))}}})";
            value = function.IsSignature ? signature : $"!DIDerivedType(tag: DW_TAG_pointer_type, baseType: {DebugNode(signature)}, size: {TargetInfo.Default.PointerBits})";
        }
        else if (type is NamedTypeExpression named && (_typeChecker.GetStructByIdentity(named.Name) != null || _typeChecker.GetClassByIdentity(named.Name) != null))
        {
            var cls = _typeChecker.GetClassByIdentity(named.Name);
            var fields = cls != null ? cls.Fields.Select(f => (f.Name, f.Type)) : _typeChecker.GetStructByIdentity(named.Name)!.Fields;
            long offset = cls != null ? TargetInfo.Default.PointerBytes : 0;
            var members = new List<string>();
            if (cls != null)
                members.Add(DebugNode($"!DIDerivedType(tag: DW_TAG_member, name: \"__vptr\", scope: {id}, baseType: {DebugType(new PointerTypeExpression(new NamedTypeExpression("void", null, 0), false, 0))}, size: {TargetInfo.Default.PointerBits}, offset: 0, flags: DIFlagArtificial)"));
            foreach (var field in fields)
            {
                int alignment = _typeChecker.GetTypeAlignment(field.Type);
                offset = (offset + alignment - 1) & -alignment;
                int size = _typeChecker.GetTypeSize(field.Type);
                members.Add(DebugNode($"!DIDerivedType(tag: DW_TAG_member, name: {DebugString(field.Name)}, scope: {id}, baseType: {DebugType(field.Type)}, size: {size * 8L}, offset: {offset * 8})"));
                offset += size;
            }
            value = $"distinct !DICompositeType(tag: {(cls != null ? "DW_TAG_class_type" : "DW_TAG_structure_type")}, name: {DebugString(key)}, size: {_typeChecker.GetTypeSize(type) * 8L}, elements: !{{{string.Join(", ", members)}}}, identifier: {DebugString(key)})";
        }
        else
        {
            string encoding = key is "float" or "double" ? "DW_ATE_float" : key == "bool" ? "DW_ATE_boolean" : key is "byte" or "ushort" or "uint" or "ulong" or "nuint" or "char" ? "DW_ATE_unsigned" : "DW_ATE_signed";
            value = $"!DIBasicType(name: {DebugString(key)}, size: {_typeChecker.GetTypeSize(type) * 8L}, encoding: {encoding})";
        }
        _debugNodes[int.Parse(id[1..])] = id + " = " + value;
        return id;
    }

    private void EmitDebugVariable(string name, TypeExpression type, string address, AstNode source, int argument = 0)
    {
        if (!_debugSymbols || !_functions.TryPeek(out var function) || function.DebugScope == null) return;
        // A ref binding's address is the referenced storage, rather than a slot holding a pointer.
        if (type is ReferenceTypeExpression reference) type = reference.Inner;
        string variable = DebugNode($"!DILocalVariable(name: {DebugString(name)}, scope: {function.DebugScope}, file: {DebugFile(source)}, line: {Math.Max(1, source.Line)}, type: {DebugType(type)}" + (argument > 0 ? $", arg: {argument}" : "") + ")");
        Emit($"    call void @llvm.dbg.declare(metadata {EmitType(type)}* {address}, metadata {variable}, metadata !DIExpression())");
    }

    private void EmitDebugThis(string address)
    {
        if (!_debugSymbols || CurrentFunction.DebugSource is not { } source || CurrentFunction.DebugThisType is not { } owner) return;
        EmitDebugVariable("this", new PointerTypeExpression(NamedTypeExpression.Resolved(owner, source.Line), false, source.Line), address, source, 1);
    }

    private string DebugNode(string value)
    {
        string id = "!" + _debugNodes.Count;
        _debugNodes.Add(id + " = " + value);
        return id;
    }

    private static string DebugString(string value) => "\"" + string.Concat(Encoding.UTF8.GetBytes(value).Select(b =>
        b is >= 32 and < 127 && b != '"' && b != '\\' ? ((char)b).ToString() : "\\" + b.ToString("X2"))) + "\"";

    private string DebugFile(AstNode source)
    {
        string path = source.Span.Source?.Path ?? "<generated>";
        if (_debugFiles.TryGetValue(path, out string? file)) return file;
        string full = path.StartsWith('<') ? path : Path.GetFullPath(path);
        string checksum = "";
        if (source.Span.Source is { } snapshot)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(snapshot.Text);
            // ReadAllText removes an encoding preamble. Match the saved file's
            // bytes (including a BOM) when it still contains this snapshot.
            if (!path.StartsWith('<') && File.Exists(full) && File.ReadAllText(full) == snapshot.Text)
                bytes = File.ReadAllBytes(full);
            checksum = $", checksumkind: CSK_SHA256, checksum: \"{Convert.ToHexString(SHA256.HashData(bytes))}\"";
        }
        file = DebugNode($"!DIFile(filename: {DebugString(Path.GetFileName(full))}, directory: {DebugString(Path.GetDirectoryName(full) ?? "")}{checksum})");
        _debugFiles.Add(path, file);
        return file;
    }

    private string DebugFunction(string linkageName, AstNode source)
    {
        string file = DebugFile(source);
        _debugUnit ??= DebugNode($"distinct !DICompileUnit(language: DW_LANG_C_plus_plus, file: {file}, producer: \"gflat\", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug)");
        IEnumerable<Parameter> parameters = source switch
        {
            MethodDeclaration method => method.Parameters,
            ConstructorDeclaration constructor => constructor.Parameters,
            OperatorDeclaration op => op.Parameters,
            LambdaExpression lambda => lambda.Parameters,
            _ => []
        };
        TypeExpression? returnType = source switch
        {
            MethodDeclaration method => method.ReturnType,
            OperatorDeclaration op => op.ReturnType,
            LambdaExpression lambda => _typeChecker.GetLambdaReturnType(lambda),
            _ => null
        };
        var types = new[] { returnType == null ? "null" : DebugType(returnType) }.Concat(parameters.Select(p => DebugType(p.Type)));
        string type = DebugNode($"!DISubroutineType(types: !{{{string.Join(", ", types)}}})");
        string? owner = CurrentFunction.DebugThisType ?? _currentClass?.Name ?? _currentStruct?.Name;
        string name;
        if (source is MethodDeclaration methodDeclaration)
        {
            string prefix = _typeChecker.TryGetMethodOwner(methodDeclaration, out string methodOwner, out _)
                ? _typeChecker.DisplayTypeName(NamedTypeExpression.Resolved(methodOwner, source.Line))
                : _typeChecker.GetFunctionNamespace(methodDeclaration).Replace("$", "::");
            string member = methodDeclaration.StringLiteralPrefix is { } literal ? "operator " + literal + "\"\"" : methodDeclaration.Name;
            name = (prefix.Length > 0 ? prefix + "::" : "") + member;
        }
        else if (owner != null && source is ConstructorDeclaration or DestructorDeclaration or OperatorDeclaration)
        {
            string typeName = _typeChecker.DisplayTypeName(NamedTypeExpression.Resolved(owner, source.Line));
            string member = source switch
            {
                ConstructorDeclaration constructor => constructor.Name,
                DestructorDeclaration destructor => "~" + destructor.Name,
                OperatorDeclaration op => "operator " + op.OperatorSymbol,
                _ => ""
            };
            name = typeName + "::" + member;
        }
        else name = source is LambdaExpression ? "<lambda>" : linkageName;
        int line = Math.Max(1, source.Line);
        return DebugNode($"distinct !DISubprogram(name: {DebugString(name)}, linkageName: {DebugString(linkageName)}, scope: {file}, file: {file}, line: {line}, type: {type}, scopeLine: {line}, spFlags: DISPFlagDefinition, unit: {_debugUnit})");
    }

    private void SetDebugLocation(AstNode source)
    {
        if (!_debugSymbols || !_functions.TryPeek(out var function) || function.DebugScope == null) return;
        var key = (function.DebugScope, Math.Max(1, source.Line), Math.Max(1, source.Span.Column));
        if (!_debugLocations.TryGetValue(key, out string? location))
            _debugLocations[key] = location = DebugNode($"!DILocation(line: {key.Item2}, column: {key.Item3}, scope: {key.Item1})");
        function.DebugLocation = location;
    }

    private void EmitDebugStatement(AstNode source, Action emit)
    {
        if (!_functions.TryPeek(out var function)) { emit(); return; }
        string? previous = function.DebugLocation;
        SetDebugLocation(source);
        try { emit(); }
        finally { function.DebugLocation = previous; }
    }

    private string DebugMetadata()
    {
        if (_debugUnit == null) return "";
        string version = DebugNode("!{i32 2, !\"Debug Info Version\", i32 3}");
        string format = DebugNode(WindowsEh ? "!{i32 2, !\"CodeView\", i32 1}" : "!{i32 2, !\"Dwarf Version\", i32 4}");
        return $"\ndeclare void @llvm.dbg.declare(metadata, metadata, metadata)\n!llvm.dbg.cu = !{{{_debugUnit}}}\n!llvm.module.flags = !{{{version}, {format}}}\n" + string.Join('\n', _debugNodes) + "\n";
    }
}
