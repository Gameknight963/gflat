namespace gflat;

/// <summary>Source selection and code generation options for a compilation.</summary>
public sealed record CompilationOptions
{
    public bool NoStdLib { get; init; }

    /// <summary>
    /// Give definitions other than main internal linkage when building a complete executable.
    /// Leave false when emitting a module for other native object files to call by name.
    /// </summary>
    public bool InternalizeFunctions { get; init; }
}
