using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace gflat.Projects;

/// <summary>The private MSBuild shipped with the CLI and language server, independent of installed SDKs.</summary>
public static class MsBuildHost
{
    private static IReadOnlyDictionary<string, (string? Original, string Bundled)>? buildEnvironment;
    private static readonly Lazy<bool> initialized = new(() =>
    {
        string engine = Path.Combine(AppContext.BaseDirectory, "MSBuild.dll");
        if (!File.Exists(engine))
            throw new IOException("The bundled MSBuild engine is missing. Reinstall the gflat toolchain.");
        string sdks = Path.Combine(AppContext.BaseDirectory, "Sdks");
        foreach (string file in new[] { "Sdk.props", "Sdk.targets" })
            if (!File.Exists(Path.Combine(sdks, "Gflat.Sdk", "Sdk", file)))
                throw new IOException("The bundled Gflat.Sdk is missing. Reinstall the gflat toolchain.");
        // Process-local only. In particular, don't inherit Visual Studio's engine
        // paths when it starts the compiler or language server.
        var overrides = new Dictionary<string, (string? Original, string Bundled)> {
            ["MSBUILD_EXE_PATH"] = (Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH"), engine),
            ["MSBuildExtensionsPath"] = (Environment.GetEnvironmentVariable("MSBuildExtensionsPath"), AppContext.BaseDirectory),
            ["MSBuildSDKsPath"] = (Environment.GetEnvironmentVariable("MSBuildSDKsPath"), sdks)
        };
        // Publish before mutating the environment so user launches can restore
        // their own environment even while another thread initializes the host.
        Volatile.Write(ref buildEnvironment, overrides);
        foreach (var (name, value) in overrides) Environment.SetEnvironmentVariable(name, value.Bundled);
        return true;
    });
    private static readonly object buildLock = new();

    public static void Initialize() => _ = initialized.Value;

    /// <summary>Removes our private engine overrides from a user's child process only.
    /// Does not initialize MSBuild or change the current process environment.</summary>
    public static void RestoreUserProcessEnvironment(IDictionary<string, string?> environment)
    {
        var snapshot = Volatile.Read(ref buildEnvironment);
        if (snapshot == null) return;
        foreach (var (name, value) in snapshot)
        {
            // Preserve deliberate changes made by an embedding host after initialization.
            if (!environment.TryGetValue(name, out string? current) || current != value.Bundled) continue;
            if (value.Original == null) environment.Remove(name);
            else environment[name] = value.Original;
        }
    }

    internal static Dictionary<string, string> CreateGlobalProperties()
    {
        // Both SDK entry files and explicit imports use the running toolchain as
        // a fallback; environment/project selections keep their normal priority.
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ProjectSdk.FindRoot(AppContext.BaseDirectory) is string sdk)
            properties["GflatDefaultSdkRoot"] = ProjectCollection.Escape(sdk);
        return properties;
    }

    public static int Build(string projectPath, string target, IDictionary<string, string> properties)
    {
        Initialize();
        // MSBuild permits one active build per manager. Serialize CLI calls made
        // by an embedding host (the standalone CLI already builds one project at a time).
        lock (buildLock)
        {
            try
            {
                var globals = CreateGlobalProperties();
                foreach (var property in properties) globals[property.Key] = property.Value;
                using var collection = new ProjectCollection(globals);
                using var manager = new BuildManager();
                var parameters = new BuildParameters(collection)
                {
                    MaxNodeCount = 1,
                    EnableNodeReuse = false,
                    ShutdownInProcNodeOnBuildFinish = true,
                    Loggers = [new ConsoleLogger(LoggerVerbosity.Minimal)],
                };
                var project = collection.LoadProject(projectPath);
                var request = new BuildRequestData(project.CreateProjectInstance(), [target]);
                var result = manager.Build(parameters, request);
                if (result.Exception != null) Console.Error.WriteLine(result.Exception.Message);
                return result.OverallResult == BuildResultCode.Success ? 0 : 1;
            }
            catch (InvalidProjectFileException error)
            {
                throw new ProjectException(error.ProjectFile, error.BaseMessage, error.LineNumber, error.ColumnNumber);
            }
        }
    }
}
