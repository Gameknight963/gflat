using System.Text;
using System.Text.Json;

namespace gflat;

internal static partial class ForeignImporter
{
    private sealed record NativeValueMember(string Key, string Entry, string Native, string Declaration);

    private static IEnumerable<NativeValueMember> NativeValueMembers(string qualified, string receiver, string tag,
        NativeValueTraits traits, IEnumerable<NativeConstructor> constructors, IReadOnlySet<string> records,
        Func<string> nextEntry)
    {
        string name = "@" + qualified.Split("::").Last();
        string native = tag + " ::" + qualified;
        int separator = qualified.LastIndexOf("::", StringComparison.Ordinal);
        string scope = separator < 0 ? "" : qualified[..separator];
        foreach (var constructor in constructors)
        {
            string entry = nextEntry();
            var bridge = CreateCallBridge(entry, "cpp", scope, "void", "void", constructor.Parameters,
                constructor.NativeParameters, records, arguments =>
                    "(void)::new (static_cast<void*>(__self)) " + native + "(" + string.Join(", ", arguments) + ")",
                receiver, native, constructor.ParameterNames, constructor.IsNoThrow);
            string parameters = string.Join(", ", constructor.Parameters.Select((parameter, index) =>
                parameter + " " + constructor.ParameterNames[index]));
            yield return new(".value-constructor(" + string.Join(",", constructor.Parameters) + ")", entry,
                "\n#include <new>\n" + bridge.Native,
                bridge.Extern + "\nstruct " + name + " { public " + name + "(" + parameters + ")" +
                (constructor.IsNoThrow ? " nothrow" : "") + " { " + bridge.Body + " } }");
        }
        if (traits.CanCopy)
        {
            string entry = nextEntry();
            string noThrow = traits.CopyNoThrow ? " noexcept" : "";
            string hostNoThrow = traits.CopyNoThrow ? " nothrow" : "";
            yield return new(".value-copy", entry,
                "\n#include <new>\nextern \"C\" void " + entry + "(" + native + "* destination, const " + native + "* source)" + noThrow +
                " { ::new (static_cast<void*>(destination)) " + native + "(*source); }",
                "private extern \"C\" void " + entry + "(" + receiver + "* destination, immutable(" + receiver + ")* source)" + hostNoThrow +
                ";\nstruct " + name + " { public copy " + name + "(immutable(" + receiver + ")* source)" + hostNoThrow +
                " { " + entry + "(this, source); } }");
        }
        {
            string entry = nextEntry();
            yield return new(".value-move", entry,
                "\n#include <new>\nextern \"C\" void " + entry + "(" + native + "* destination, " + native + "* source) noexcept" +
                " { ::new (static_cast<void*>(destination)) " + native + "(static_cast<" + native + "&&>(*source));" +
                " using Object = " + native + "; source->~Object(); }",
                "private extern \"C\" void " + entry + "(" + receiver + "* destination, " + receiver + "* source) nothrow;\n" +
                "struct " + name + " { public move " + name + "(" + receiver + "* source) nothrow { " + entry + "(this, source); } }");
        }
        {
            string entry = nextEntry();
            yield return new(".value-destructor", entry,
                "extern \"C\" void " + entry + "(" + native + "* object) noexcept { using Object = " + native + "; object->~Object(); }",
                "private extern \"C\" void " + entry + "(" + receiver + "* object) nothrow;\n" +
                "struct " + name + " { ~" + name + "() { " + entry + "(this); } }");
        }
    }

    private static IReadOnlyDictionary<string, NativeValueTraits> ReadNativeValueTraits(
        ImportedRecord[] records, string source, string directory, string compiler, List<string> arguments,
        SourceSpan span, CancellationToken cancellation)
    {
        var candidates = records.Where(record => record.OpaqueLayout is { Size: not null }).ToArray();
        if (candidates.Length == 0) return new Dictionary<string, NativeValueTraits>();
        // Constructibility includes destructor access. A native new-expression does not,
        // and may separately be blocked by a class-specific allocation operator.
        var query = new StringBuilder(source).AppendLine()
            .AppendLine("template<class T> auto __gflat_native_default_allocation(int) -> decltype(new T(), char());")
            .AppendLine("template<class> long __gflat_native_default_allocation(...);")
            .AppendLine("enum __gflat_native_value_traits {");
        for (int index = 0; index < candidates.Length; index++)
        {
            string type = candidates[index].Tag + " ::" + candidates[index].Qualified;
            query.Append("copy_").Append(index).Append(" = __is_constructible(").Append(type).Append(", const ").Append(type).AppendLine("&),");
            query.Append("copy_nothrow_").Append(index).Append(" = __is_nothrow_constructible(").Append(type).Append(", const ").Append(type).AppendLine("&),");
            query.Append("move_").Append(index).Append(" = __is_nothrow_constructible(").Append(type).Append(", ").Append(type).AppendLine("&&),");
            query.Append("destroy_").Append(index).Append(" = __is_nothrow_destructible(").Append(type).AppendLine("),");
            query.Append("default_").Append(index).Append(" = __is_constructible(").Append(type).AppendLine("),");
            query.Append("default_nothrow_").Append(index).Append(" = __is_nothrow_constructible(").Append(type).AppendLine("),");
            query.Append("allocate_default_").Append(index).Append(" = sizeof(__gflat_native_default_allocation<")
                .Append(type).AppendLine(">(0)) == sizeof(char),");
        }
        query.AppendLine("};");
        string path = Path.Combine(directory, "value-traits.cpp");
        File.WriteAllText(path, query.ToString());
        var options = arguments.Concat(new[] { "-x", "c++", "-fsyntax-only", "-Xclang", "-ast-dump=json",
            "-Xclang", "-ast-dump-filter", "-Xclang", "__gflat_native_value_traits", path });
        using var ast = JsonDocument.Parse(Run(compiler, options, span, cancellation));
        var values = new Dictionary<string, bool>();
        foreach (var member in ast.RootElement.GetProperty("inner").EnumerateArray())
            if (Text(member, "kind") == "EnumConstantDecl")
                values.Add(Text(member, "name"), Constant(member));
        return candidates.Select((record, index) => (record.Qualified, Traits: new NativeValueTraits(
            values["copy_" + index], values["copy_nothrow_" + index], values["move_" + index], values["destroy_" + index],
            values["default_" + index], values["default_nothrow_" + index], values["allocate_default_" + index])))
            .ToDictionary(item => item.Qualified, item => item.Traits);

        static bool Constant(JsonElement node)
        {
            if (node.TryGetProperty("value", out var value)) return value.ToString() is "true" or "1";
            if (node.TryGetProperty("inner", out var children))
                foreach (var child in children.EnumerateArray())
                    if (Text(child, "kind") == "ConstantExpr") return Constant(child);
                    else if (child.TryGetProperty("inner", out _) || child.TryGetProperty("value", out _)) return Constant(child);
            throw new InvalidOperationException("Clang did not evaluate native value traits");
        }
    }
}
