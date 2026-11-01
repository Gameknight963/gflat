namespace gflat.Tests;

internal static class TypeDescriptionTestSources
{
    internal static IEnumerable<SourceFile> Include(string source)
        => new[] { "core/TypeInfo.gf", "core/FieldInfo.gf", "core/type_descriptions.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("type-description-test.gf", source));
}
