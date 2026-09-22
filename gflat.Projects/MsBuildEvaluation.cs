using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace gflat.Projects;

internal static class MsBuildEvaluation
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ProjectModel Load(string path, string configuration, string platform)
    {
        try
        {
            using var collection = new ProjectCollection(new Dictionary<string, string>
            { ["Configuration"] = configuration, ["Platform"] = platform });
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
            var globs = project.GetAllGlobs("Compile").Select(g => g.MsBuildGlob).ToArray();
            // MSBuild's evaluated glob expressions match paths relative to the project.
            return new ProjectModel(path, kind, Path.GetFullPath(target, Path.GetDirectoryName(path)!),
                Files("Compile"), Files("ProjectReference"),
                project.Imports.Select(i => i.ImportedProject.FullPath).Distinct(ProjectModel.Paths).ToArray(),
                file => globs.Any(g => g.IsMatch(Path.GetRelativePath(Path.GetDirectoryName(path)!, file))));
        }
        catch (InvalidProjectFileException error)
        { throw new ProjectException(string.IsNullOrEmpty(error.ProjectFile) ? path : error.ProjectFile, error.BaseMessage, error.LineNumber, error.ColumnNumber); }
    }
}
