using gflat.CompileExceptions;

namespace gflat;

/// <summary>The small set of library sources included automatically in every compilation.</summary>
public static class StandardLibrary
{
    public static IReadOnlyList<string> AutomaticFiles { get; } = Array.AsReadOnly(new[] {
        "core/Attribute.gf", "core/Exception.gf", "core/BadAllocException.gf", "libc/Exceptions/unhandled.gf", "core/ITextWriter.gf", "core/IFormattable.gf", "core/TypeInfo.gf", "core/IGarbageCollector.gf", "libc/Memory/allocation.gf"
    });

    public static bool IsSource(string? path, string relativePath)
    {
        if (path == null || path.StartsWith('<')) return false;
        string normalized = path.Replace('\\', '/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // Explicit Standard inputs (including unsaved editor snapshots) take precedence
        // over the bundled copy. Identifying the full Standard-relative path avoids
        // mistaking an unrelated Attribute.gf for the compiler's library base.
        return normalized.Equals("Standard/" + relativePath, comparison) ||
            normalized.EndsWith("/Standard/" + relativePath, comparison);
    }

    public static SourceFile Load(string relativePath)
    {
        if (!AutomaticFiles.Contains(relativePath)) throw new ArgumentException("Not an automatic library source", nameof(relativePath));
        string path = Path.Combine(AppContext.BaseDirectory, "Standard", relativePath.Replace('/', Path.DirectorySeparatorChar));
        try { return new SourceFile(path, File.ReadAllText(path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new TypeCheckException($"Cannot read required standard-library source '{path}': {error.Message}. Restore the Standard directory shipped with the compiler.", 0);
        }
    }
}
