using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace gflat.Projects;

internal static class MsBuildEvaluation
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ProjectModel Load(string path, string configuration, string platform, int? exceptionReserveBytes, int? optimizationLevel)
    {
        try
        {
            var properties = new Dictionary<string, string>
            { ["Configuration"] = configuration, ["Platform"] = platform };
            // Default only: environment/project GflatSdkRoot remains authoritative.
            if (ProjectSdk.FindRoot(AppContext.BaseDirectory) is string sdk)
                properties["GflatDefaultSdkRoot"] = ProjectCollection.Escape(sdk);
            if (exceptionReserveBytes.HasValue) properties["GflatExceptionReserveBytes"] = exceptionReserveBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (optimizationLevel.HasValue) properties["GflatOptimizationLevel"] = optimizationLevel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var collection = new ProjectCollection(properties);
            var project = new Project(path, null, null, collection, ProjectLoadSettings.RecordEvaluatedItemElements);
            var kind = project.GetPropertyValue("GflatProjectKind") switch
            {
                "Executable" => ProjectKind.Executable,
                "SourceLibrary" => ProjectKind.SourceLibrary,
                _ => throw new ProjectException(path, "GflatProjectKind must be Executable or SourceLibrary. Import Gflat.props and Gflat.targets.")
            };
            string target = project.GetPropertyValue("TargetPath");
            if (string.IsNullOrEmpty(target)) throw new ProjectException(path, "TargetPath is missing. Import Gflat.targets.");
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
            return new ProjectModel(path, kind, Path.GetFullPath(target, Path.GetDirectoryName(path)!),
                Files("Compile"), Files("ProjectReference"),
                project.Imports.Select(i => i.ImportedProject.FullPath).Distinct(ProjectModel.Paths).ToArray(),
                file => globs.Any(g => g.IsMatch(Path.GetRelativePath(Path.GetDirectoryName(path)!, file))), reserveBytes, optimization,
                project.GetPropertyValue("GflatSdkRoot") is { Length: > 0 } root ? Path.GetFullPath(root, Path.GetDirectoryName(path)!) : null);
        }
        catch (InvalidProjectFileException error)
        {
            string message = error.BaseMessage;
            if (error.ErrorCode == "MSB4019" && (message.Contains("Gflat.props") || message.Contains("Gflat.targets")))
                message += " Set GflatSdkRoot to a directory containing build/Gflat.props, build/Gflat.targets and std/std.gfproj, or use a gflat installation with bundled SDK assets.";
            throw new ProjectException(string.IsNullOrEmpty(error.ProjectFile) ? path : error.ProjectFile, message, error.LineNumber, error.ColumnNumber);
        }
    }
}
