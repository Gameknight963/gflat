using System.Text;
using System.Text.RegularExpressions;

namespace gflat;

internal static partial class ForeignImporter
{
    private sealed record CallBridge(string Native, string Extern, string Body);

    private static string[] ParameterNames(IEnumerable<System.Text.Json.JsonElement> parameters)
    {
        var names = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "this", "__gflat_result" };
        foreach (var parameter in parameters)
        {
            string name = Text(parameter, "name");
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") || !used.Add(name))
            {
                name = "arg" + names.Count;
                while (!used.Add(name)) name += "_";
            }
            names.Add("@" + name);
        }
        return names.ToArray();
    }

    private static string? RecordValueType(string type, IReadOnlySet<string> records)
    {
        while (type.StartsWith("readonly(", StringComparison.Ordinal) && type.EndsWith(')')) type = type[9..^1];
        return records.Contains(type) ? type : null;
    }

    private static CallBridge CreateCallBridge(string entry, string language, string scope, string result,
        string nativeResult, IReadOnlyList<string> parameters, IReadOnlyList<string> nativeParameters,
        IReadOnlySet<string> records, Func<IReadOnlyList<string>, string> call,
        string? receiver = null, string? nativeReceiver = null, IReadOnlyList<string>? parameterNames = null)
    {
        string? recordResult = RecordValueType(result, records);
        var externParameters = new List<string>();
        var nativeDeclarations = new List<string>();
        var hostValues = new List<string>();
        var nativeValues = new List<string>();
        if (recordResult != null)
        {
            externParameters.Add("void* __result");
            nativeDeclarations.Add("void* __result");
            hostValues.Add("&__gflat_result");
        }
        if (receiver != null)
        {
            externParameters.Add(receiver + "* __self");
            nativeDeclarations.Add(nativeReceiver + "* __self");
            hostValues.Add("this");
        }
        for (int index = 0; index < parameters.Count; index++)
        {
            string name = "__arg" + index, native = nativeParameters[index];
            bool reference = parameters[index].StartsWith("ref ", StringComparison.Ordinal);
            if (reference)
            {
                externParameters.Add(parameters[index][4..] + "* " + name);
                nativeDeclarations.Add("__typeof__(" + native[..native.LastIndexOf('&')].Trim() + ")* " + name);
                hostValues.Add("&" + (parameterNames?[index] ?? name));
                nativeValues.Add("*" + name);
                continue;
            }
            string? record = RecordValueType(parameters[index], records);
            externParameters.Add((record != null ? "readonly(" + record + ")*" : parameters[index]) + " " + name);
            nativeDeclarations.Add((record != null ? "const __typeof__(" + native + ")*" :
                Regex.IsMatch(native, @"\(\s*\*") ? "__typeof__(" + native + ")" : native) + " " + name);
            hostValues.Add((record == null ? "" : "&") + (parameterNames?[index] ?? name));
            nativeValues.Add((record == null ? "" : "*") + name);
        }
        string expression = call(nativeValues);
        var definition = new StringBuilder();
        if (language == "cpp")
            foreach (string part in scope.Split("::", StringSplitOptions.RemoveEmptyEntries))
                definition.Append("namespace ").Append(part).Append(" { ");
        if (language == "cpp") definition.Append("extern \"C\" ");
        definition.Append(recordResult != null ? "void" : language == "cpp" ? "auto" :
                Regex.IsMatch(nativeResult, @"\(\s*\*") ? "__typeof__(" + nativeResult + ")" : nativeResult)
            .Append(' ').Append(entry).Append('(').Append(string.Join(", ", nativeDeclarations)).Append(')');
        if (language == "cpp" && recordResult == null) definition.Append(" -> decltype(").Append(expression).Append(')');
        definition.Append(" { ");
        if (recordResult != null)
            definition.Append("__typeof__(").Append(expression).Append(") __value = ").Append(expression)
                .Append("; __builtin_memcpy(__result, &__value, sizeof(__value));");
        else definition.Append(result == "void" ? "" : "return ").Append(expression).Append(';');
        definition.Append(" }");
        if (language == "cpp")
            foreach (string _ in scope.Split("::", StringSplitOptions.RemoveEmptyEntries)) definition.Append(" }");
        string invocation = entry + "(" + string.Join(", ", hostValues) + ");";
        string body = recordResult != null
            ? recordResult + " __gflat_result = default(" + recordResult + "); " + invocation + " return __gflat_result;"
            : (result == "void" ? "" : "return ") + invocation;
        return new(definition.ToString(), "private extern \"C\" " + (recordResult != null ? "void" : result) + " " + entry +
            "(" + string.Join(", ", externParameters) + ");", body);
    }
}
