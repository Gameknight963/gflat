using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using gflat.Projects;

namespace gflat.Build.Tasks;

public sealed class GflatWritePreprocessorInputs : Task
{
    [Required] public string ManifestPath { get; set; } = "";
    public ITaskItem[] Directories { get; set; } = [];
    public ITaskItem[] Defines { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            new PreprocessorInputs(Directories.Select(item => item.ItemSpec), Defines.Select(item => item.ItemSpec)).Write(ManifestPath);
            return true;
        }
        catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
        { Log.LogError("Preprocessor inputs: " + error.Message); return false; }
    }
}
