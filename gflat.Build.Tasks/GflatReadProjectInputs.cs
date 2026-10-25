using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using gflat.Projects;

namespace gflat.Build.Tasks;

/// <summary>Read imports and references using the invoking engine's actual global properties.</summary>
public sealed class GflatReadProjectInputs : Task
{
    [Required] public string ProjectFile { get; set; } = "";
    [Required] public string Configuration { get; set; } = "";
    [Required] public string Platform { get; set; } = "";
    [Output] public ITaskItem[] ProjectInputs { get; set; } = [];
    public string NativeLinkOverrides { get; set; } = "";
    [Output] public ITaskItem[] NativeLibraries { get; set; } = [];
    [Output] public ITaskItem[] NativeLibraryDirectories { get; set; } = [];
    [Output] public ITaskItem[] NativeLibraryFiles { get; set; } = [];
    [Output] public bool HasLogicalLibraries { get; set; }

    public override bool Execute()
    {
        try
        {
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ((IBuildEngine6)BuildEngine).GetGlobalProperties())
                globals[pair.Key] = pair.Value;
            globals["Configuration"] = Configuration;
            globals["Platform"] = Platform;
            var comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var visited = new HashSet<string>(comparer);
            var inputs = new HashSet<string>(comparer);
            var nativeProjects = new List<NativeLinkProject>();
            using (var collection = new ProjectCollection())
            {
                void Visit(string file, bool asReference)
                {
                    file = Path.GetFullPath(file);
                    if (!visited.Add(file)) return;
                    var properties = new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase);
                    if (asReference) properties["GflatAsReference"] = "true";
                    var project = new Project(file, properties, null, collection);
                    inputs.Add(file);
                    foreach (var import in project.Imports) inputs.Add(import.ImportedProject.FullPath);
                    var references = project.GetItems("ProjectReference").Select(reference => reference.GetMetadataValue("FullPath")).ToArray();
                    var libraries = new List<NativeLibraryInput>();
                    var directories = new List<string>();
                    var survivingItems = new HashSet<ProjectItem>(project.Items);
                    foreach (var item in project.AllEvaluatedItems.Where(survivingItems.Contains))
                    {
                        if (item.ItemType.Equals("NativeLibrary", StringComparison.OrdinalIgnoreCase)) libraries.Add(new NativeLibraryInput("Name", item.EvaluatedInclude));
                        else if (item.ItemType.Equals("NativeLibraryFile", StringComparison.OrdinalIgnoreCase)) libraries.Add(new NativeLibraryInput("File", item.EvaluatedInclude));
                        else if (item.ItemType.Equals("NativeLibraryDirectory", StringComparison.OrdinalIgnoreCase)) directories.Add(item.EvaluatedInclude);
                    }
                    nativeProjects.Add(new NativeLinkProject(file, references, NativeLinkInputs.Create(libraries, directories, Path.GetDirectoryName(file)!)));
                    foreach (string reference in references) Visit(reference, true);
                }
                Visit(ProjectFile, false);
            }
            ProjectInputs = inputs.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            NativeLinkInputs nativeInputs = NativeLinkProjectOrder.Aggregate(Path.GetFullPath(ProjectFile), nativeProjects);
            if (NativeLinkOverrides.Length != 0) nativeInputs = nativeInputs.Append(NativeLinkInputs.Read(NativeLinkOverrides));
            NativeLibraries = nativeInputs.Libraries.Select(library =>
            {
                var item = new TaskItem(library.Value);
                item.SetMetadata("Kind", library.Kind);
                return (ITaskItem)item;
            }).ToArray();
            NativeLibraryDirectories = nativeInputs.Directories.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            NativeLibraryFiles = nativeInputs.Libraries.Where(library => library.Kind == "File").Select(library => (ITaskItem)new TaskItem(library.Value)).ToArray();
            HasLogicalLibraries = nativeInputs.HasLogicalLibraries;
            return true;
        }
        catch (InvalidProjectFileException error)
        {
            Log.LogError(null, error.ErrorCode, null, error.ProjectFile, error.LineNumber, error.ColumnNumber,
                error.EndLineNumber, error.EndColumnNumber, error.BaseMessage);
            return false;
        }
        catch (Exception error) when (error is ArgumentException || error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
        {
            Log.LogError("Native link inputs: " + error.Message);
            return false;
        }
    }
}

/// <summary>Write only after the targets have validated all generated/input path collisions.</summary>
public sealed class GflatWriteNativeLinkInputs : Task
{
    [Required] public string ManifestPath { get; set; } = "";
    public ITaskItem[] Libraries { get; set; } = [];
    public ITaskItem[] Directories { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var inputs = new NativeLinkInputs(Libraries.Select(item => new NativeLibraryInput(item.GetMetadata("Kind"), item.ItemSpec)), Directories.Select(item => item.ItemSpec));
            inputs.Write(ManifestPath);
            return true;
        }
        catch (Exception error) when (error is ArgumentException || error is IOException || error is InvalidDataException || error is UnauthorizedAccessException)
        {
            Log.LogError("Native link inputs: " + error.Message);
            return false;
        }
    }
}
