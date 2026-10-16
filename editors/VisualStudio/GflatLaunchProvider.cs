using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;

namespace gflat.VisualStudio;

// CPS and Visual Studio own startup selection, building and process launch.
[ExportDebugger(GflatLaunch.SchemaName)]
[AppliesTo("Gflat")]
internal sealed class GflatLaunchProvider : DebugLaunchProviderBase
{
    private readonly ConfiguredProject project;

    [ImportingConstructor]
    public GflatLaunchProvider(ConfiguredProject project) : base(project) { this.project = project; }

    public override async Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
    {
        if ((launchOptions & DebugLaunchOptions.NoDebug) == 0) return false;
        var provider = project.Services.ProjectPropertiesProvider;
        if (provider == null) return false;
        var properties = provider.GetCommonProperties();
        return GflatLaunch.CanLaunch(launchOptions,
            await properties.GetEvaluatedPropertyValueAsync("OutputType"),
            await properties.GetEvaluatedPropertyValueAsync("GflatEmitIR"));
    }

    public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
    {
        GflatLaunch.RequireNoDebug(launchOptions);
        var provider = project.Services.ProjectPropertiesProvider ??
            throw new InvalidOperationException("CPS cannot read the gflat project's launch properties.");
        var properties = provider.GetCommonProperties();
        var settings = GflatLaunch.CreateSettings(launchOptions,
            await properties.GetEvaluatedPropertyValueAsync("OutputType"),
            await properties.GetEvaluatedPropertyValueAsync("GflatEmitIR"),
            await properties.GetEvaluatedPropertyValueAsync("TargetPath"),
            await properties.GetEvaluatedPropertyValueAsync("GflatRunArguments"),
            await properties.GetEvaluatedPropertyValueAsync("GflatRunWorkingDirectory"),
            Path.GetDirectoryName(project.UnconfiguredProject.FullPath)!);
        return new IDebugLaunchSettings[] { settings };
    }
}

internal static class GflatLaunch
{
    internal const string SchemaName = "GflatLocal";

    internal static bool CanLaunch(DebugLaunchOptions options, string kind, string emitIr) =>
        (options & DebugLaunchOptions.NoDebug) != 0 &&
        string.Equals(kind, "Executable", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(emitIr, "true", StringComparison.OrdinalIgnoreCase);

    internal static void RequireNoDebug(DebugLaunchOptions options)
    {
        if ((options & DebugLaunchOptions.NoDebug) == 0)
            throw new NotSupportedException("gflat source debugging is not available yet. Use Start Without Debugging (Ctrl+F5).");
    }

    internal static DebugLaunchSettings CreateSettings(DebugLaunchOptions options, string kind, string emitIr,
        string targetPath, string arguments, string workingDirectory, string projectDirectory)
    {
        RequireNoDebug(options);
        if (!CanLaunch(options, kind, emitIr))
            throw new InvalidOperationException("Only executable gflat projects with native output can run.");
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("The gflat project has no TargetPath.");
        string executable = Resolve(projectDirectory, targetPath);
        if (!File.Exists(executable))
            throw new FileNotFoundException("The gflat executable was not built. Build the project before running it.", executable);
        string directory = string.IsNullOrWhiteSpace(workingDirectory) ? projectDirectory : Resolve(projectDirectory, workingDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The gflat working directory does not exist: " + directory);
        return new DebugLaunchSettings(options)
        {
            LaunchOperation = DebugLaunchOperation.CreateProcess,
            LaunchDebugEngineGuid = DebuggerEngines.NativeOnlyEngine,
            Executable = executable,
            Arguments = arguments ?? string.Empty,
            CurrentDirectory = directory
        };
    }

    private static string Resolve(string directory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
}
