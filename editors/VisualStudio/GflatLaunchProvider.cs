using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace gflat.VisualStudio;

// CPS and Visual Studio own startup selection, building and process launch.
[ExportDebugger(GflatLaunch.SchemaName)]
[AppliesTo("Gflat")]
internal sealed class GflatLaunchProvider : DebugLaunchProviderBase
{
    private readonly ConfiguredProject project;
    private readonly IServiceProvider serviceProvider;

    [ImportingConstructor]
    public GflatLaunchProvider(ConfiguredProject project,
        [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider) : base(project)
    {
        this.project = project;
        this.serviceProvider = serviceProvider;
    }

    public override async Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
    {
        var provider = project.Services.ProjectPropertiesProvider;
        if (provider == null) return false;
        var properties = provider.GetCommonProperties();
        return GflatLaunch.CanLaunch(launchOptions,
            await properties.GetEvaluatedPropertyValueAsync("OutputType"),
            await properties.GetEvaluatedPropertyValueAsync("GflatEmitIR"));
    }

    public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
    {
        var provider = project.Services.ProjectPropertiesProvider ??
            throw new InvalidOperationException("CPS cannot read the G\u266d project's launch properties.");
        var properties = provider.GetCommonProperties();
        var settings = GflatLaunch.CreateSettings(launchOptions,
            await properties.GetEvaluatedPropertyValueAsync("OutputType"),
            await properties.GetEvaluatedPropertyValueAsync("GflatEmitIR"),
            await properties.GetEvaluatedPropertyValueAsync("TargetPath"),
            await properties.GetEvaluatedPropertyValueAsync("GflatRunArguments"),
            await properties.GetEvaluatedPropertyValueAsync("GflatRunWorkingDirectory"),
            Path.GetDirectoryName(project.UnconfiguredProject.FullPath)!);
        await ThreadingService.SwitchToUIThread();
        settings.Project = project.UnconfiguredProject.Services.HostObject as IVsHierarchy;
        if (serviceProvider.GetService(typeof(SVsShellDebugger)) is IVsDebugger10 debugger)
        {
            debugger.IsIntegratedConsoleEnabled(out bool enabled);
            GflatLaunch.ConfigureConsole(settings, enabled);
        }
        return new IDebugLaunchSettings[] { settings };
    }
}

internal static class GflatLaunch
{
    internal const string SchemaName = "GflatLocal";

    internal static void ConfigureConsole(DebugLaunchSettings settings, bool enabled)
    {
        if (enabled) settings.LaunchOptions |= DebugLaunchOptions.IntegratedConsole;
    }

    internal static bool CanLaunch(DebugLaunchOptions options, string kind, string emitIr) =>
        string.Equals(kind, "Executable", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(emitIr, "true", StringComparison.OrdinalIgnoreCase);

    internal static DebugLaunchSettings CreateSettings(DebugLaunchOptions options, string kind, string emitIr,
        string targetPath, string arguments, string workingDirectory, string projectDirectory)
    {
        if (!CanLaunch(options, kind, emitIr))
            throw new InvalidOperationException("Only executable G\u266d projects with native output can run.");
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("The G\u266d project has no TargetPath.");
        string executable = Resolve(projectDirectory, targetPath);
        if (!File.Exists(executable))
            throw new FileNotFoundException("The G\u266d executable was not built. Build the project before running it.", executable);
        string directory = string.IsNullOrWhiteSpace(workingDirectory) ? projectDirectory : Resolve(projectDirectory, workingDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The G\u266d working directory does not exist: " + directory);
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
