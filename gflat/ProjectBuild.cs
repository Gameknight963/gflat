using gflat.Projects;

namespace gflat;

internal static class ProjectBuild
{
    internal static int Run(ProjectModel project, string target, string configuration, string platform,
        bool emitIr = false, string? output = null, string? clang = null, int? exceptionReserveBytes = null, int? optimizationLevel = null)
    {
        // Explicit source-list invocations in the targets avoid recursively building the project.
        string assembly = typeof(Program).Assembly.Location;
        var arguments = new List<string> { "msbuild", project.FilePath, "-nologo", "-v:minimal", "-t:" + target };
        void Property(string name, string value) => arguments.Add("-p:" + name + "=" + Escape(value));
        Property("Configuration", configuration);
        Property("Platform", platform);
        if (project.SdkRoot != null) Property("GflatDefaultSdkRoot", project.SdkRoot);
        Property("GflatCompilerCommand", "dotnet \"" + assembly + "\"");
        if (emitIr) Property("GflatEmitIR", "true");
        if (output != null) Property(emitIr ? "GflatIROutput" : "TargetPath", output);
        if (clang != null) Property("GflatClang", clang);
        if (exceptionReserveBytes.HasValue) Property("GflatExceptionReserveBytes", exceptionReserveBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (optimizationLevel.HasValue) Property("GflatOptimizationLevel", optimizationLevel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var result = NativeToolchain.Run("dotnet", arguments, 120000);
        Console.Write(result.StandardOutput);
        Console.Error.Write(result.StandardError);
        return result.ExitCode;
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace(";", "%3B")
        .Replace(",", "%2C").Replace("$", "%24").Replace("@", "%40").Replace("'", "%27");
}
