using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using gflat.Projects;

namespace gflat.VisualStudio;

[Export(typeof(IVsLoggerEventProcessor2))]
[AppliesTo("Gflat")]
internal sealed class GflatBuildFailureTracker : IVsLoggerEventProcessor2
{
    private readonly UnconfiguredProject project;

    [ImportingConstructor]
    public GflatBuildFailureTracker(UnconfiguredProject project) { this.project = project; }

    public async Task<bool> HandleEventAsync(BuildEventArgs buildEventArgs, string message, TextWriter outputWindowWriter)
    {
        if (buildEventArgs is BuildErrorEventArgs ||
            buildEventArgs is ProjectFinishedEventArgs projectFinished && !projectFinished.Succeeded ||
            buildEventArgs is BuildFinishedEventArgs buildFinished && !buildFinished.Succeeded)
        {
            try { BuildUpToDateState.RecordBuildFailure(project.FullPath, buildEventArgs.Timestamp.ToUniversalTime()); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                await outputWindowWriter.WriteLineAsync("Could not persist the failed-build state: " + error.Message);
            }
        }
        // Keep the original error and Visual Studio's normal presentation.
        return false;
    }

    public Task FlushAsync() => Task.CompletedTask;
}
