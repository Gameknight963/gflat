namespace gflat;

/// <summary>A verbatim C or C++ source segment from one source translation unit.</summary>
public sealed record ForeignSource(string Language, SourceSpan Span);

/// <summary>A foreign translation unit, including compiler-generated ABI entry points.</summary>
public sealed record ForeignTranslationUnit(string Language, string Text, IReadOnlyList<string> Arguments)
{
    public IReadOnlyDictionary<string, string> Bridges { get; init; } = new Dictionary<string, string>();
}

public sealed record EmittedProgram(string Ir, IReadOnlyList<ForeignTranslationUnit> ForeignUnits)
{
    public BinaryInterface? Interface { get; init; }
}

public sealed record ForeignDependency(string Path, string? ContentHash)
{
    public bool IsCurrent() => ContentHash != null && Hash(Path) == ContentHash;

    internal static ForeignDependency Capture(string path) => new(path, Hash(path));

    private static string? Hash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
