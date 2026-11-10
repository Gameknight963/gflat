using System.Text.Json;
using System.Text.RegularExpressions;

namespace gflat;

internal static partial class ForeignImporter
{
    private enum CallableRole { Member, Construction, Destruction, ValueMember }
    private sealed record NativeConstructor(string Symbol, string[] Parameters, string[] NativeParameters, string[] ParameterNames,
        bool IsNoThrow = false);

    private static IEnumerable<NativeConstructor> RecordConstructors(JsonElement record, Func<string, string?> type)
    {
        if (record.TryGetProperty("definitionData", out var data) && data.TryGetProperty("isAbstract", out var abstractType) &&
            abstractType.GetBoolean()) return [];
        var constructors = new List<NativeConstructor>();
        string access = Text(record, "tagUsed") == "class" ? "private" : "public";
        bool explicitConstructor = false;
        foreach (var child in record.GetProperty("inner").EnumerateArray())
        {
            string kind = Text(child, "kind");
            if (kind == "AccessSpecDecl") { access = Text(child, "access"); continue; }
            if (kind != "CXXConstructorDecl") continue;
            bool implicitConstructor = child.TryGetProperty("isImplicit", out _);
            explicitConstructor |= !implicitConstructor;
            if ((!implicitConstructor && access != "public") || IsDeletedNativeMember(child) ||
                child.TryGetProperty("variadic", out _)) continue;
            var parameters = child.TryGetProperty("inner", out var children)
                ? children.EnumerateArray().Where(node => Text(node, "kind") == "ParmVarDecl").ToArray() : [];
            // Implicit copy/move construction is outside the imported lifetime API.
            if (implicitConstructor && parameters.Length != 0) continue;
            var native = parameters.Select(parameter => parameter.GetProperty("type"))
                .Select(parameter => Text(parameter, "desugaredQualType", Text(parameter, "qualType"))).ToArray();
            var mapped = native.Select(type).ToArray();
            if (mapped.Any(parameter => parameter == null)) continue;
            string signature = Text(child.GetProperty("type"), "qualType");
            bool noThrow = FunctionContract(child, FunctionShape(signature)?.Qualifiers ?? "").IsNoThrow;
            constructors.Add(new(Text(child, "mangledName"), mapped.Select(parameter => parameter!).ToArray(), native, ParameterNames(parameters), noThrow));
        }
        if (!explicitConstructor && constructors.All(constructor => constructor.Parameters.Length != 0) &&
            !record.GetProperty("inner").EnumerateArray().Any(child => Text(child, "kind") == "CXXDestructorDecl" && IsDeletedNativeMember(child)) &&
            record.TryGetProperty("definitionData", out data) && data.TryGetProperty("defaultCtor", out var defaultCtor) &&
            defaultCtor.TryGetProperty("exists", out var exists) && exists.GetBoolean())
            constructors.Add(new("implicit-default-constructor", [], [], []));
        return constructors.GroupBy(constructor => string.Join(",", constructor.Parameters.Select(parameter =>
                Regex.Replace(parameter, "readonly\\(|\\)", ""))))
            .Where(group => group.Count() == 1).Select(group => group.Single());
    }

    private static bool HasPublicNativeDestructor(JsonElement record)
    {
        string access = Text(record, "tagUsed") == "class" ? "private" : "public";
        foreach (var child in record.GetProperty("inner").EnumerateArray())
        {
            string kind = Text(child, "kind");
            if (kind == "AccessSpecDecl") access = Text(child, "access");
            if (kind == "CXXDestructorDecl")
                return !IsDeletedNativeMember(child) && (child.TryGetProperty("isImplicit", out _) || access == "public");
        }
        return true;
    }

    private static bool IsDeletedNativeMember(JsonElement node)
        => node.TryGetProperty("explicitlyDeleted", out _) || Text(node, "explicitlyDefaulted") == "deleted";
}
