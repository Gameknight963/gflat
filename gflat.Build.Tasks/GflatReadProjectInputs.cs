using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace gflat.Build.Tasks;

/// <summary>Read imports and references using the invoking engine's actual global properties.</summary>
public sealed class GflatReadProjectInputs : Task
{
    [Required] public string ProjectFile { get; set; } = "";
    [Required] public string Configuration { get; set; } = "";
    [Required] public string Platform { get; set; } = "";
    [Output] public ITaskItem[] ProjectInputs { get; set; } = [];

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
                    foreach (var reference in project.GetItems("ProjectReference"))
                        Visit(reference.GetMetadataValue("FullPath"), true);
                }
                Visit(ProjectFile, false);
            }
            ProjectInputs = inputs.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            return true;
        }
        catch (InvalidProjectFileException error)
        {
            Log.LogError(null, error.ErrorCode, null, error.ProjectFile, error.LineNumber, error.ColumnNumber,
                error.EndLineNumber, error.EndColumnNumber, error.BaseMessage);
            return false;
        }
    }
}
