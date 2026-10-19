using gflat.Projects;

namespace gflat;

internal static class ProjectBuild
{
    internal static int Run(ProjectModel project, string target, string configuration, string platform,
        bool emitIr = false, string? output = null, string? clang = null, int? exceptionReserveBytes = null, int? optimizationLevel = null,
        ProjectGraph? sourceGraph = null, bool noStdLib = false, bool? debugSymbols = null)
    {
        // Explicit source-list invocations in the targets avoid recursively building the project.
        string assembly = typeof(Program).Assembly.Location;
        var properties = new Dictionary<string, string>();
        void Property(string name, string value) => properties[name] = ProjectSdk.EscapePropertyValue(value);
        Property("Configuration", configuration);
        Property("Platform", platform);
        if (noStdLib) Property("NoStdLib", "true");
        if (debugSymbols.HasValue) Property("DebugSymbols", debugSymbols.Value ? "true" : "false");
        if (project.SdkRoot != null) Property("GflatSdkRoot", project.SdkRoot);
        string appHost = Path.Combine(Path.GetDirectoryName(assembly)!, OperatingSystem.IsWindows() ? "gflat.exe" : "gflat");
        string compiler = File.Exists(appHost) ? appHost : assembly;
        Property("GflatCompilerPath", compiler);
        sourceGraph ??= ProjectGraph.Load(project.FilePath,
            (path, asReference) => ProjectModel.Load(path, configuration, platform, exceptionReserveBytes, optimizationLevel, noStdLib ? true : null, asReference, debugSymbols));
        Property("GflatProjectInputs", string.Join(";", sourceGraph.Projects
            .SelectMany(item => item.Imports.Prepend(item.FilePath)).Distinct(ProjectModel.Paths)
            .Select(ProjectSdk.EscapePropertyValue)));
        if (project.Kind == ProjectKind.Executable && !emitIr && (target is "Build" or "Rebuild"))
        {
            try { Property("GflatDefaultClangInput", NativeToolchain.FindClang(clang)); }
            catch (FileNotFoundException) { /* The compiler reports the missing driver if needed. */ }
        }
        if (emitIr) Property("GflatEmitIR", "true");
        if (output != null) Property(emitIr ? "GflatIROutput" : "TargetPath", output);
        if (clang != null) Property("GflatClang", clang);
        if (exceptionReserveBytes.HasValue) Property("GflatExceptionReserveBytes", exceptionReserveBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (optimizationLevel.HasValue) Property("GflatOptimizationLevel", optimizationLevel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Isolate MSBuild's working-directory/environment changes from embedding
        // hosts. The same apphost runs our bundled engine; no dotnet/MSBuild installation.
        var arguments = new List<string> { "--msbuild", project.FilePath, target };
        foreach (var property in properties) { arguments.Add(property.Key); arguments.Add(property.Value); }
        string driver = appHost;
        if (!File.Exists(driver))
        {
            driver = "dotnet"; // Framework-dependent developer builds without an apphost.
            arguments.Insert(0, assembly);
        }
        var result = NativeToolchain.Run(driver, arguments, 120000);
        Console.Write(result.StandardOutput);
        Console.Error.Write(result.StandardError);
        return result.ExitCode;
    }

    // Private worker entry point, invoked by Run.
    internal static int RunEngine(string[] args)
    {
        if (args.Length < 2 || args.Length % 2 != 0) throw new ArgumentException("Invalid MSBuild worker arguments.");
        var properties = new Dictionary<string, string>();
        for (int i = 2; i < args.Length; i += 2) properties.Add(args[i], args[i + 1]);
        return MsBuildHost.Build(args[0], args[1], properties);
    }
}
