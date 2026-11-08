using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace gflat;

internal static partial class ForeignImporter
{
    private sealed record RecordLayout(int Size, int Alignment, int[] Offsets);
    private sealed record ImportedRecord(string Qualified, string Type, string Declaration);

    private static Dictionary<string, RecordLayout> ReadRecordLayouts(string output, out string json)
    {
        int start = output.StartsWith('{') ? 0 : output.IndexOf("\n{", StringComparison.Ordinal) + 1;
        json = output[start..];
        var layouts = new Dictionary<string, RecordLayout>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(output[..start],
            @"Type: (?:struct|class) (?<name>[^\r\n]+)\r?\n\s*Layout: <ASTRecordLayout\s+Size:(?<size>\d+)\s+Alignment:(?<alignment>\d+).*?FieldOffsets: \[(?<fields>[^\]]*)\]>",
            RegexOptions.Singleline))
        {
            var numbers = new[] { match.Groups["size"].Value, match.Groups["alignment"].Value }
                .Concat(match.Groups["fields"].Value.Split(',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToArray();
            var values = new int[numbers.Length];
            bool valid = true;
            for (int index = 0; index < numbers.Length; index++)
                valid &= int.TryParse(numbers[index], NumberStyles.None, CultureInfo.InvariantCulture, out values[index]);
            if (valid) layouts[match.Groups["name"].Value] = new(values[0], values[1], values[2..]);
        }
        return layouts;
    }

    private static ImportedRecord? ImportRecord(JsonElement node, string scope,
        IReadOnlyDictionary<string, RecordLayout> layouts, Func<string, string?> type)
    {
        string name = Text(node, "name");
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") || Text(node, "tagUsed") is not ("struct" or "class") ||
            !node.TryGetProperty("completeDefinition", out var complete) || !complete.GetBoolean() ||
            node.TryGetProperty("bases", out _) || !node.TryGetProperty("inner", out var children)) return null;
        if (Text(node, "kind") == "CXXRecordDecl" &&
            (!node.TryGetProperty("definitionData", out var data) ||
             !data.TryGetProperty("isPOD", out var pod) || !pod.GetBoolean())) return null;

        string qualified = scope.Length == 0 ? name : scope + "::" + name;
        if (!layouts.TryGetValue(qualified, out var layout)) return null;
        var fields = new List<string>();
        var offsets = new List<int>();
        int size = 0, alignment = 1;
        string access = Text(node, "tagUsed") == "class" ? "private" : "public";
        foreach (var child in children.EnumerateArray())
        {
            string kind = Text(child, "kind");
            if (kind.EndsWith("Attr", StringComparison.Ordinal)) return null;
            if (kind == "AccessSpecDecl") { access = Text(child, "access"); continue; }
            if (kind is "CXXConstructorDecl" or "CXXDestructorDecl" && !child.TryGetProperty("isImplicit", out _)) return null;
            if (kind != "FieldDecl") continue;
            string field = Text(child, "name");
            if (access != "public" || !Regex.IsMatch(field, "^[A-Za-z_][A-Za-z0-9_]*$") ||
                child.TryGetProperty("isBitfield", out _) || child.TryGetProperty("inner", out _)) return null;
            var nativeType = child.GetProperty("type");
            string? mapped = type(Text(nativeType, "desugaredQualType", Text(nativeType, "qualType")));
            if (mapped == null) return null;
            string scalar = mapped.Replace("readonly(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal);
            int bytes = mapped.Contains('*') ? TargetInfo.Default.PointerBytes : scalar switch
            {
                "bool" or "char" or "sbyte" or "byte" => 1,
                "short" or "ushort" => 2,
                "int" or "uint" or "float" => 4,
                "long" or "ulong" or "double" => 8,
                _ => 0
            };
            if (bytes == 0) return null;
            alignment = Math.Max(alignment, bytes);
            size = (size + bytes - 1) & -bytes;
            offsets.Add(size * 8);
            size += bytes;
            fields.Add("public " + mapped + " @" + field + ";");
        }
        size = (size + alignment - 1) & -alignment;
        if (fields.Count == 0 || layout.Size != size * 8 || layout.Alignment != alignment * 8 ||
            !layout.Offsets.SequenceEqual(offsets)) return null;
        string escaped = string.Join("::", qualified.Split("::").Select(part => "@" + part));
        string declaration = "public struct @" + name + " { " + string.Join(" ", fields) + " }";
        if (scope.Length != 0) declaration = "namespace " + string.Join("::", scope.Split("::").Select(part => "@" + part)) +
            " { " + declaration + " }";
        return new(qualified, escaped, declaration);
    }
}
