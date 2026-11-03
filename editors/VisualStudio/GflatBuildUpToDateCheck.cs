using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Build;
using gflat.Projects;

namespace gflat.VisualStudio;

[Export(typeof(IBuildUpToDateCheckProvider))]
[AppliesTo("Gflat")]
internal sealed class GflatBuildUpToDateCheck : IBuildUpToDateCheckProvider2
{
    private readonly ConfiguredProject project;
    private readonly IProjectLockService locks;
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

    [ImportingConstructor]
    public GflatBuildUpToDateCheck(ConfiguredProject project, IProjectLockService locks)
    {
        this.project = project;
        this.locks = locks;
    }

    public Task<bool> IsUpToDateCheckEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> IsUpToDateAsync(BuildAction buildAction, TextWriter logger, CancellationToken cancellationToken)
        => IsUpToDateAsync(buildAction, logger, ImmutableDictionary<string, string>.Empty, cancellationToken);

    public async Task<bool> IsUpToDateAsync(BuildAction buildAction, TextWriter logger,
        IImmutableDictionary<string, string> globalProperties, CancellationToken cancellationToken)
    {
        if (buildAction != BuildAction.Build) return false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var input = await locks.ReadLockAsync(async access =>
            {
                var evaluated = await access.GetProjectAsync(project, cancellationToken);
                string state = evaluated.GetPropertyValue("_GflatUpToDateState");
                var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var recorded = new HashSet<string>(BuildUpToDateState.RecordedGlobalPropertyNames(state), StringComparer.OrdinalIgnoreCase);
                foreach (string name in recorded)
                    if (!BuildSubmissionProperty(name) && !EvaluationOnlyProperty(name)) required[name] = evaluated.GetPropertyValue(name);
                // CPS evaluates the editor with design-time switches that are
                // deliberately absent from the native build request.
                foreach (var pair in evaluated.GlobalProperties)
                    if (!BuildSubmissionProperty(pair.Key) && !EvaluationOnlyProperty(pair.Key)) required[pair.Key] = evaluated.GetPropertyValue(pair.Key);
                foreach (var pair in globalProperties)
                {
                    if (EvaluationOnlyProperty(pair.Key)) continue;
                    // CPS applies baseline SDK defaults to an already evaluated
                    // ProjectInstance with SetProperty. They do not rewrite its
                    // imports or derived compiler command. Actual SDK globals
                    // recorded by MSBuild, and current evaluation globals above,
                    // remain part of the strict comparison.
                    if (!recorded.Contains(pair.Key) &&
                        new[] { "GflatSdkRoot", "GflatCompilerPath", "GflatBundledCompiler" }.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) continue;
                    required[pair.Key] = ProjectCollection.Unescape(pair.Value);
                }
                return new
                {
                    Xml = evaluated.Xml.RawXml,
                    RequiredGlobals = required,
                    State = state,
                    Disabled = evaluated.GetPropertyValue("DisableFastUpToDateCheck").Equals("true", StringComparison.OrdinalIgnoreCase)
                };
            }, cancellationToken);
            if (input.Disabled || input.State.Length == 0) return false;
            bool current = BuildUpToDateState.IsUpToDate(input.State, project.UnconfiguredProject.FullPath, input.Xml, cancellationToken, input.RequiredGlobals, logger);
            if (current) await logger.WriteLineAsync("Project is up-to-date.");
            return current;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is FormatException || error is XmlException || error is InvalidProjectFileException)
        {
            await logger.WriteLineAsync("Up-to-date check could not read the build state: " + error.Message);
            return false;
        }
        finally { gate.Release(); }
    }

    private static bool BuildSubmissionProperty(string name) =>
        new[] { "SolutionDir", "SolutionExt", "SolutionFileName", "SolutionName", "SolutionPath", "BuildingInsideVisualStudio", "CurrentSolutionConfigurationContents", "DefineExplicitDefaults", "DevEnvDir", "LangID", "LangName", "VSIDEResolvedNonMSBuildProjectOutputs", "BuildProjectReferences", "BuildingProject", "DesignTimeBuild" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool EvaluationOnlyProperty(string name) =>
        new[] { "DesignTimeBuild", "BuildingProject", "SkipCompilerExecution", "ProvideCommandLineArgs", "CompileDesignTime" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);
}
