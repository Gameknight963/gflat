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
    public string PreprocessorOverrides { get; set; } = "";
    [Output] public ITaskItem[] IncludeDirectories { get; set; } = [];
    [Output] public ITaskItem[] Defines { get; set; } = [];
    [Output] public ITaskItem[] DeclarationSources { get; set; } = [];
    [Output] public ITaskItem[] BinaryInterfaces { get; set; } = [];
    [Output] public ITaskItem[] RuntimeFiles { get; set; } = [];
    [Output] public string CompilerRuntimeLibrary { get; set; } = "MultiThreaded";

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
            var includeDirectories = new List<string>();
            var definitions = new List<string>();
            var declarationScopes = new Dictionary<string, string>(comparer);
            var binaryInterfaces = new List<string>();
            var runtimeFiles = new List<string>();
            var runtimeProfiles = new List<string>();
            using (var collection = new ProjectCollection())
            {
                void Visit(string file, bool asReference)
                {
                    file = Path.GetFullPath(file);
                    if (!visited.Add(file)) return;
                    var properties = new Dictionary<string, string>(globals, StringComparer.OrdinalIgnoreCase);
                    if (asReference) properties["GflatAsReference"] = "true";
                    bool native = file.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase);
                    if (native) properties["Platform"] = "x64";
                    var project = new Project(file, properties, null, collection);
                    inputs.Add(file);
                    foreach (var import in project.Imports) inputs.Add(import.ImportedProject.FullPath);
                    if (!asReference && project.GetPropertyValue("RuntimeLibrary") is { Length: > 0 } rootRuntime) runtimeProfiles.Add(rootRuntime);
                    if (native)
                    {
                        if (project.GetPropertyValue("MSBuildRuntimeType") != "Full")
                            throw new ArgumentException("Visual C++ project references require Visual Studio MSBuild.");
                        string kind = project.GetPropertyValue("ConfigurationType");
                        if (kind != "StaticLibrary" && kind != "DynamicLibrary") throw new ArgumentException("Referenced Visual C++ projects must produce a native library.");
                        runtimeProfiles.AddRange(project.GetItems("ClCompile").Where(item => item.GetMetadataValue("ExcludedFromBuild") != "true")
                            .Select(item => item.GetMetadataValue("RuntimeLibrary")).Where(value => value.Length != 0));
                        var output = new System.Collections.Hashtable();
                        properties["BuildProjectReferences"] = "false";
                        if (!BuildEngine.BuildProjectFile(file, ["GetProjectInfoForReference"], properties, output))
                            throw new IOException("Cannot read native project reference information: " + file);
                        var information = output["GetProjectInfoForReference"] as ITaskItem[] ?? [];
                        var nativeLibraries = information.Where(item => item.GetMetadata("FileType") == "lib")
                            .Select(item => new NativeLibraryInput("File", item.GetMetadata("FullPath"))).ToArray();
                        includeDirectories.AddRange(information.Where(item => item.GetMetadata("FileType") == "IncludeDirectory").Select(item => item.ItemSpec));
                        if (kind == "DynamicLibrary") runtimeFiles.Add(project.GetPropertyValue("TargetPath"));
                        nativeProjects.Add(new NativeLinkProject(file, [], new NativeLinkInputs(nativeLibraries, [])));
                        return;
                    }
                    string outputType = project.GetPropertyValue("OutputType");
                    if (asReference && (outputType == "StaticLibrary" || outputType == "DynamicLibrary"))
                    {
                        var reference = new BinaryProjectReference(project.GetPropertyValue("TargetPath"), outputType == "DynamicLibrary");
                        binaryInterfaces.Add(reference.InterfacePath);
                        inputs.Add(reference.InterfacePath);
                        inputs.Add(reference.InterfacePath + ".gfh");
                        runtimeFiles.AddRange(reference.RuntimeFiles);
                        if (reference.RuntimeLibrary != null) runtimeProfiles.Add(reference.RuntimeLibrary);
                        nativeProjects.Add(new NativeLinkProject(file, [], reference.NativeInputs));
                        return;
                    }
                    includeDirectories.AddRange(project.GetItems("IncludeDirectory").Select(item => item.GetMetadataValue("FullPath")));
                    if (!asReference) definitions.AddRange(project.GetItems("Define").Select(item => item.EvaluatedInclude));
                    string scope = PreprocessorInputs.ParseDeclarationScope(project.GetPropertyValue("DeclarationScope"));
                    foreach (var item in project.GetItems("Compile"))
                        declarationScopes[item.GetMetadataValue("FullPath")] = PreprocessorInputs.ParseDeclarationScope(
                            item.GetMetadataValue("DeclarationScope") is { Length: > 0 } itemScope ? itemScope : scope);
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
            var profiles = runtimeProfiles.Select(gflat.Projects.RuntimeLibrary.Parse).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (profiles.Length > 1) throw new ArgumentException("Referenced libraries use incompatible RuntimeLibrary settings.");
            CompilerRuntimeLibrary = profiles.FirstOrDefault() ?? "MultiThreaded";
            if (runtimeFiles.Distinct(comparer).GroupBy(Path.GetFileName, comparer).Any(group => group.Count() > 1))
                throw new ArgumentException("Referenced runtime libraries have conflicting filenames.");
            BinaryInterfaces = binaryInterfaces.Distinct(comparer).Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            RuntimeFiles = runtimeFiles.Distinct(comparer).Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            var preprocessing = new PreprocessorInputs(includeDirectories, definitions, declarationScopes);
            if (PreprocessorOverrides.Length != 0) preprocessing = preprocessing.Append(PreprocessorInputs.Read(PreprocessorOverrides));
            IncludeDirectories = preprocessing.Directories.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            Defines = preprocessing.Defines.Select(pair => (ITaskItem)new TaskItem(pair.Key + "=" + pair.Value)).ToArray();
            DeclarationSources = preprocessing.DeclarationScopes.Select(pair =>
            {
                var item = new TaskItem(pair.Key);
                item.SetMetadata("DeclarationScope", pair.Value);
                return (ITaskItem)item;
            }).ToArray();
            NativeLinkInputs nativeInputs = NativeLinkProjectOrder.Aggregate(Path.GetFullPath(ProjectFile), nativeProjects);
            if (NativeLinkOverrides.Length != 0) nativeInputs = nativeInputs.Append(NativeLinkInputs.Read(NativeLinkOverrides));
            NativeLibraries = nativeInputs.Libraries.Select(library =>
            {
                var item = new TaskItem(library.Value);
                item.SetMetadata("Kind", library.Kind);
                return (ITaskItem)item;
            }).ToArray();
            NativeLibraryDirectories = nativeInputs.Directories.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            NativeLibraryFiles = nativeInputs.Libraries.Where(library => library.Kind == "File").Select(library => library.Value)
                .Concat(runtimeFiles).Distinct(comparer).Select(path => (ITaskItem)new TaskItem(path)).ToArray();
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
            Log.LogError("Project inputs: " + error.Message);
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
