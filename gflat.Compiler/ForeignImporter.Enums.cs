using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace gflat;

internal static partial class ForeignImporter
{
    private sealed record ImportedEnum(string Qualified, string Type, string Declaration);

    private static ImportedEnum? ImportEnum(JsonElement node, string scope, Func<string, string?> type)
    {
        string name = Text(node, "name");
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") ||
            !node.TryGetProperty("fixedUnderlyingType", out var underlying) ||
            !node.TryGetProperty("inner", out var children)) return null;
        string? storage = type(Text(underlying, "desugaredQualType", Text(underlying, "qualType")));
        if (storage is not ("sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong")) return null;
        var members = new List<string>();
        BigInteger next = 0;
        foreach (var member in children.EnumerateArray().Where(child => Text(child, "kind") == "EnumConstantDecl"))
        {
            string memberName = Text(member, "name");
            if (!Regex.IsMatch(memberName, "^[A-Za-z_][A-Za-z0-9_]*$")) return null;
            if (member.TryGetProperty("inner", out var expressions))
            {
                string? value = expressions.EnumerateArray().Select(ConstantValue).FirstOrDefault(value => value != null);
                if (value == null || !BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out next)) return null;
            }
            members.Add("@" + memberName + " = " + next.ToString(CultureInfo.InvariantCulture) + (storage == "ulong" ? "ul" : "l"));
            next++;
        }
        string qualified = scope.Length == 0 ? name : scope + "::" + name;
        string declaration = "public enum @" + name + " : " + storage + " { " + string.Join(", ", members) + " }";
        if (scope.Length != 0)
            declaration = "namespace " + EscapeNativeName(scope) + " { " + declaration + " }";
        return new(qualified, EscapeNativeName(qualified), declaration);
    }

    private static string EscapeNativeName(string name) => string.Join("::", name.Split("::").Select(part => "@" + part));

    private static string? ConstantValue(JsonElement node)
    {
        if (Text(node, "kind") == "ConstantExpr" && node.TryGetProperty("value", out var value)) return value.GetString();
        if (node.TryGetProperty("inner", out var children))
            foreach (var child in children.EnumerateArray())
                if (ConstantValue(child) is { } constant) return constant;
        return null;
    }
}
