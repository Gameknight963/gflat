using System.Text.Json;
using System.Text.RegularExpressions;

namespace gflat;

internal static partial class ForeignImporter
{
    private sealed record RecordMethod(string Name, string Symbol, string Result, string[] Parameters,
        string[] NativeParameters, string[] ParameterNames, bool ReadOnly, bool IsStatic);

    private static IEnumerable<RecordMethod> RecordMethods(JsonElement record, Func<string, string?> type, Func<string, string?> parameterType)
    {
        var methods = new List<RecordMethod>();
        string access = Text(record, "tagUsed") == "class" ? "private" : "public";
        foreach (var child in record.GetProperty("inner").EnumerateArray())
        {
            string kind = Text(child, "kind");
            if (kind == "AccessSpecDecl") { access = Text(child, "access"); continue; }
            if (kind != "CXXMethodDecl" || access != "public" ||
                child.TryGetProperty("isImplicit", out _) || child.TryGetProperty("explicitlyDeleted", out _) ||
                child.TryGetProperty("variadic", out _)) continue;
            string name = Text(child, "name"), signature = Text(child.GetProperty("type"), "qualType");
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") || name == "reinterpret") continue;
            int opening = signature.IndexOf('('), closing = -1, depth = 0;
            for (int index = opening; index >= 0 && index < signature.Length; index++)
            {
                if (signature[index] == '(') depth++;
                else if (signature[index] == ')' && --depth == 0) { closing = index; break; }
            }
            if (opening < 0 || closing < opening) continue;
            string suffix = signature[(closing + 1)..].Trim();
            if (!Regex.IsMatch(suffix, @"^(?:const\s*)?(?:noexcept(?:\([^()]*\))?)?$")) continue;
            string? result = type(signature[..opening]);
            if (result == null) continue;
            var parameters = child.TryGetProperty("inner", out var body)
                ? body.EnumerateArray().Where(node => Text(node, "kind") == "ParmVarDecl").ToArray() : [];
            var nativeParameters = parameters.Select(parameter => parameter.GetProperty("type"))
                .Select(parameter => Text(parameter, "desugaredQualType", Text(parameter, "qualType"))).ToArray();
            var mapped = nativeParameters.Select(parameterType).ToArray();
            if (mapped.Any(parameter => parameter == null)) continue;
            methods.Add(new(name, Text(child, "mangledName"), result, mapped.Select(parameter => parameter!).ToArray(),
                nativeParameters, ParameterNames(parameters), suffix.StartsWith("const", StringComparison.Ordinal), Text(child, "storageClass") == "static"));
        }
        // Native overloads erased to one host signature need a richer selection rule.
        return methods.GroupBy(method => method.Name).Where(group => group.Select(method => method.IsStatic).Distinct().Count() == 1)
            .SelectMany(group => group).GroupBy(method => method.Name + "(" + string.Join(",", method.Parameters.Select(parameter =>
                Regex.Replace(parameter, "readonly\\(|\\)", ""))) + ")")
            .Where(group => group.Count() == 1).Select(group => group.Single());
    }
}
