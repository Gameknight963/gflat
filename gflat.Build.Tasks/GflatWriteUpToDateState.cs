using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using gflat.Projects;

namespace gflat.Build.Tasks;

public sealed class GflatWriteUpToDateState : Task
{
    [Required] public string ProjectFile { get; set; } = "";
    [Required] public string FilePath { get; set; } = "";
    [Required] public string BuildStartedUtcTicks { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ((IBuildEngine6)BuildEngine).GetGlobalProperties()) globals[pair.Key] = pair.Value;
            DateTime started = new DateTime(long.Parse(BuildStartedUtcTicks, CultureInfo.InvariantCulture), DateTimeKind.Utc);
            BuildUpToDateState.Capture(ProjectFile, globals, started).Write(FilePath);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is System.Xml.XmlException || error is Microsoft.Build.Exceptions.InvalidProjectFileException)
        {
            // The build succeeded. A missing snapshot simply schedules the next build.
            Log.LogMessage(MessageImportance.Low, "Could not record the up-to-date state: " + error.Message);
        }
        return true;
    }
}
