namespace gflat;

/// <summary>Language inputs selected by the caller, shared by compilation and editor analysis.</summary>
public sealed record CompilationOptions
{
    public bool NoStdLib { get; init; }
}
