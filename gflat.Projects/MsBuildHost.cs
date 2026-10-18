using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;

namespace gflat.Projects;

/// <summary>The private MSBuild shipped with the CLI and language server, independent of installed SDKs.</summary>
public static class MsBuildHost
{
    private static readonly Lazy<bool> initialized = new(() =>
    {
        string engine = Path.Combine(AppContext.BaseDirectory, "MSBuild.dll");
        if (!File.Exists(engine))
            throw new IOException("The bundled MSBuild engine is missing. Reinstall the gflat toolchain.");
        // Process-local only. In particular, don't inherit Visual Studio's engine
        // paths when it starts the compiler or language server.
        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", engine);
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", AppContext.BaseDirectory);
        return true;
    });
    private static readonly object buildLock = new();

    public static void Initialize() => _ = initialized.Value;

    public static int Build(string projectPath, string target, IDictionary<string, string> properties)
    {
        Initialize();
        // MSBuild permits one active build per manager. Serialize CLI calls made
        // by an embedding host (the standalone CLI already builds one project at a time).
        lock (buildLock)
        {
            try
            {
                using var collection = new ProjectCollection(properties);
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
