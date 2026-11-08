using System.Diagnostics;
using System.Text.Json;

namespace gflat.Projects;

public static class VisualStudioMsBuild
{
    public static string FindExecutable()
    {
        if (!OperatingSystem.IsWindows()) throw new IOException("Visual C++ projects require Windows and Visual Studio MSBuild.");
        var candidates = new List<string>();
        var environment = new ProcessStartInfo().Environment;
        MsBuildHost.RestoreUserProcessEnvironment(environment);
        if (environment.TryGetValue("MSBUILD_EXE_PATH", out var configured) && configured?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            candidates.Add(configured);
        if (environment.TryGetValue("VSINSTALLDIR", out var installation) && !string.IsNullOrWhiteSpace(installation))
            candidates.Add(Path.Combine(installation, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe"));
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string root = Path.Combine(Environment.GetFolderPath(folder), "Microsoft Visual Studio");
            if (!Directory.Exists(root)) continue;
            foreach (string version in Directory.EnumerateDirectories(root))
                foreach (string edition in Directory.EnumerateDirectories(version))
                    candidates.Add(Path.Combine(edition, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe"));
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).Where(HasCppToolset)
            .OrderByDescending(path => FileVersionInfo.GetVersionInfo(path).FileMajorPart)
            .ThenByDescending(path => FileVersionInfo.GetVersionInfo(path).FileMinorPart).FirstOrDefault()
            ?? throw new IOException("Visual Studio MSBuild was not found. Install the Desktop development with C++ workload.");
    }

    private static bool HasCppToolset(string executable)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(executable)!); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "VC", "Tools", "MSVC"))) return true;
        return false;
    }

    public static (int ExitCode, string Output, string Error) Run(string project, IEnumerable<string> arguments, int timeoutMilliseconds = 120000)
    {
        var info = new ProcessStartInfo(FindExecutable()) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        MsBuildHost.RestoreUserProcessEnvironment(info.Environment);
        string sdks = Path.Combine(AppContext.BaseDirectory, "Sdks");
        if (File.Exists(Path.Combine(sdks, "Gflat.Sdk", "Sdk", "Sdk.props"))) info.Environment["MSBuildSDKsPath"] = sdks;
        info.ArgumentList.Add(Path.GetFullPath(project));
        info.ArgumentList.Add("-nologo");
        info.ArgumentList.Add("-m:1");
        info.ArgumentList.Add("-nodeReuse:false");
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Cannot start Visual Studio MSBuild.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new IOException("Visual Studio MSBuild timed out while processing " + project);
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    internal static ProjectModel LoadProject(string path, string? configuration, string? platform)
    {
        configuration ??= "Debug";
        platform = platform is null or "AnyCPU" or "Any CPU" ? "x64" : platform;
        if (!platform.Equals("x64", StringComparison.OrdinalIgnoreCase))
            throw new ProjectException(path, "Native project references currently require the x64 platform.");
        string resultPath = Path.Combine(Path.GetTempPath(), "gflat-native-project-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var result = Run(path, ["-p:Configuration=" + ProjectSdk.EscapePropertyValue(configuration), "-p:Platform=" + platform,
                "-p:BuildProjectReferences=false",
                "-getProperty:ConfigurationType,TargetPath,Configuration,Platform,MSBuildAllProjects",
                "-getItem:ClCompile,ProjectReference", "-getTargetResult:GetProjectInfoForReference", "-getResultOutputFile:" + resultPath]);
            if (result.ExitCode != 0) throw new ProjectException(path, "Native project evaluation failed:\n" + result.Output + result.Error);
            using var json = JsonDocument.Parse(File.ReadAllText(resultPath));
            var properties = json.RootElement.GetProperty("Properties");
            string Property(string name) => properties.GetProperty(name).GetString()!;
            var kind = Property("ConfigurationType") switch
            {
                "StaticLibrary" => ProjectKind.StaticLibrary,
                "DynamicLibrary" => ProjectKind.DynamicLibrary,
                _ => throw new ProjectException(path, "A referenced Visual C++ project must produce a static or dynamic library.")
            };
            var files = json.RootElement.GetProperty("Items").GetProperty("ClCompile").EnumerateArray().ToArray();
            var runtimes = files.Where(file => !file.TryGetProperty("ExcludedFromBuild", out var excluded) || excluded.GetString() != "true")
                .Where(file => file.TryGetProperty("RuntimeLibrary", out _)).Select(file => file.GetProperty("RuntimeLibrary").GetString()!)
                .Where(value => value.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (runtimes.Length > 1) throw new ProjectException(path, "Native project sources use different RuntimeLibrary settings.");
            var information = json.RootElement.GetProperty("TargetResults").GetProperty("GetProjectInfoForReference").GetProperty("Items").EnumerateArray().ToArray();
            var links = information.Where(item => item.TryGetProperty("FileType", out var type) && type.GetString() == "lib")
                .Select(item => new NativeLibraryInput("File", item.GetProperty("FullPath").GetString()!)).ToArray();
            var includes = information.Where(item => item.TryGetProperty("FileType", out var type) && type.GetString() == "IncludeDirectory")
                .Select(item => item.GetProperty("FullPath").GetString()!).ToArray();
            var model = new ProjectModel(path, kind, Property("TargetPath"), [], [],
                Property("MSBuildAllProjects").Split(';', StringSplitOptions.RemoveEmptyEntries), _ => false,
                ExceptionReserve.DefaultBytes, 0, noStdLib: true, configuration: Property("Configuration"), platform: platform,
                nativeLinkInputs: new(links, []), preprocessorInputs: new(includes, []), runtimeLibrary: runtimes.FirstOrDefault() ?? "MultiThreaded", isNative: true);
            return model;
        }
        finally { if (File.Exists(resultPath)) File.Delete(resultPath); }
    }
}
