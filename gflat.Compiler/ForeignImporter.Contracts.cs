using System.Text.Json;
using System.Text.RegularExpressions;

namespace gflat;

internal static partial class ForeignImporter
{
    private static (string Qualifiers, bool IsNoThrow) FunctionContract(JsonElement function, string qualifiers)
    {
        bool isNoThrow = function.TryGetProperty("inner", out var children) &&
            children.EnumerateArray().Any(child => Text(child, "kind") == "NoThrowAttr");
        const string attribute = "__attribute__((nothrow))";
        int start = qualifiers.Length - attribute.Length;
        // Match the function's trailing type attribute, not text inside a
        // conditional noexcept expression or a callback parameter's type.
        if (start >= 0 && qualifiers.EndsWith(attribute, StringComparison.Ordinal) &&
            (start == 0 || char.IsWhiteSpace(qualifiers[start - 1])))
        {
            qualifiers = qualifiers[..start].TrimEnd();
            isNoThrow = true;
        }
        isNoThrow = isNoThrow || Regex.IsMatch(qualifiers, @"^(?:const\s+)?noexcept(?:\(\s*(?:true|1)\s*\))?$");
        return (qualifiers, isNoThrow);
    }
}
