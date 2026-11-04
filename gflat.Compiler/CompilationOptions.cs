namespace gflat;

/// <summary>Source selection and code generation options for a compilation.</summary>
public sealed record CompilationOptions
{
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [];
    public IReadOnlyDictionary<string, string> Defines { get; init; } = new Dictionary<string, string>();
    public Func<string, SourceFile?>? IncludeLoader { get; init; }
    public Action<IReadOnlyList<string>>? DependencyObserver { get; init; }
    public bool NoStdLib { get; init; }
    public bool DebugSymbols { get; init; }

    /// <summary>
    /// Disable optimizer recognition/introduction of libc builtins in generated functions.
    /// Explicit memory intrinsics and platform ABI helpers can still require native support.
    /// Independent of source selection through NoStdLib; disabled by default.
    /// </summary>
    public bool NoBuiltins { get; init; }

    /// <summary>
    /// Emit reachable function bodies and give definitions other than main internal linkage
    /// when building a complete executable.
    /// Leave false when emitting a module for other native object files to call by name.
    /// </summary>
    public bool InternalizeFunctions { get; init; }
}
