using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace gflat.Projects;

internal static class MsBuildEvaluation
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ProjectModel Load(string path, string? configuration, string? platform, int? exceptionReserveBytes, int? optimizationLevel, bool? noStdLib, bool asReference)
    {
        try
        {
            var properties = new Dictionary<string, string>();
            if (asReference) properties["GflatAsReference"] = "true";
            if (configuration != null) properties["Configuration"] = configuration;
            if (platform != null) properties["Platform"] = platform;
            if (exceptionReserveBytes.HasValue) properties["GflatExceptionReserveBytes"] = exceptionReserveBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (optimizationLevel.HasValue) properties["GflatOptimizationLevel"] = optimizationLevel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (noStdLib.HasValue) properties["NoStdLib"] = noStdLib.Value ? "true" : "false";
            using var collection = new ProjectCollection(properties);
            var project = new Project(path, null, null, collection, ProjectLoadSettings.RecordEvaluatedItemElements);
            var kind = project.GetPropertyValue("OutputType").ToLowerInvariant() switch
            {
                "executable" => ProjectKind.Executable,
                "sourcelibrary" => ProjectKind.SourceLibrary,
                _ => throw new ProjectException(path, "OutputType must be Executable or SourceLibrary. Use <Project Sdk=\"Gflat.Sdk\">.")
            };
            if (!bool.TryParse(project.GetPropertyValue("NoStdLib"), out bool excludesStd))
                throw new ProjectException(path, "NoStdLib must be true or false.");
            string? target = kind == ProjectKind.Executable ? project.GetPropertyValue("TargetPath") : null;
            if (kind == ProjectKind.Executable && string.IsNullOrEmpty(target))
                throw new ProjectException(path, "TargetPath is missing. Use the Gflat.Sdk project SDK.");
            string[] Files(string type) => project.GetItems(type).Select(i => i.GetMetadataValue("FullPath"))
                .Distinct(ProjectModel.Paths).ToArray();
            int reserveBytes;
            try { reserveBytes = ExceptionReserve.Parse(project.GetPropertyValue("GflatExceptionReserveBytes") is { Length: > 0 } value ? value : ExceptionReserve.DefaultBytes.ToString()); }
            catch (ArgumentException error) { throw new ProjectException(path, "GflatExceptionReserveBytes: " + error.Message); }
            int optimization;
            try { optimization = OptimizationLevel.Parse(project.GetPropertyValue("GflatOptimizationLevel") is { Length: > 0 } opt ? opt : "0"); }
            catch (ArgumentException error) { throw new ProjectException(path, "GflatOptimizationLevel: " + error.Message); }
            var globs = project.GetAllGlobs("Compile").Select(g => g.MsBuildGlob).ToArray();
            // MSBuild's evaluated glob expressions match paths relative to the project.
            return new ProjectModel(path, kind, target == null ? null : Path.GetFullPath(target, Path.GetDirectoryName(path)!),
                Files("Compile"), Files("ProjectReference"),
                project.Imports.Select(i => i.ImportedProject.FullPath).Distinct(ProjectModel.Paths).ToArray(),
                file => globs.Any(g => g.IsMatch(Path.GetRelativePath(Path.GetDirectoryName(path)!, file))), reserveBytes, optimization,
                project.GetPropertyValue("GflatSdkRoot") is { Length: > 0 } root ? Path.GetFullPath(root, Path.GetDirectoryName(path)!) : null,
                excludesStd, project.GetPropertyValue("Configuration"), project.GetPropertyValue("Platform"));
        }
        catch (InvalidProjectFileException error)
        {
            string message = error.BaseMessage;
            if (error.ErrorCode == "MSB4019" && (message.Contains("Gflat.props") || message.Contains("Gflat.targets")))
                message += " Set GflatSdkRoot to a directory containing build/Gflat.props and build/Gflat.targets.";
            if (message.Contains("Gflat.Sdk") && error.ErrorCode is "MSB4236" or "MSB4276")
                message += " Install the project SDK for this MSBuild host using 'gflat sdk install' (or --sdk-directory for Visual Studio MSBuild). Installation is a separate setup step; builds do not modify MSBuild.";
            throw new ProjectException(string.IsNullOrEmpty(error.ProjectFile) ? path : error.ProjectFile, message, error.LineNumber, error.ColumnNumber);
        }
    }
}
