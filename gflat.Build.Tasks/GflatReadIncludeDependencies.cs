using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using gflat.Projects;

namespace gflat.Build.Tasks;

public sealed class GflatReadIncludeDependencies : Task
{
    [Required] public string FilePath { get; set; } = "";
    [Output] public ITaskItem[] Files { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var files = new List<ITaskItem>();
            var paths = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : [];
            PreprocessorInputs.WriteDependencyState(FilePath, paths);
            foreach (string path in paths)
            {
                if (path.Length == 0) continue;
                bool exists = File.Exists(path);
                if (exists) files.Add(new TaskItem(path));
            }
            Files = files.ToArray();
            return true;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        { Log.LogError("Include dependencies: " + error.Message); return false; }
    }
}
