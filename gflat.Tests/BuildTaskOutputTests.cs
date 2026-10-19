namespace gflat.Tests;

public sealed class BuildTaskOutputTests
{
    [Theory]
    [InlineData("net472", false)]
    [InlineData("net472", true)]
    [InlineData("net10.0", false)]
    [InlineData("net10.0", true)]
    public async Task SdkTaskFrameworksKeepDistinctPathsWhenArtifactsOutputIsEnabled(string framework, bool artifacts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "gflat.Build.Tasks/gflat.Build.Tasks.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string project = Path.Combine(directory.FullName, "gflat.Build.Tasks/gflat.Build.Tasks.csproj");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Other tests initialize our bundled engine. This child deliberately
        // evaluates a C# SDK project using the developer's installed dotnet SDK.
        foreach(string name in new[]{"MSBUILD_EXE_PATH","MSBuildExtensionsPath","MSBuildSDKsPath"})
            start.Environment.Remove(name);
        foreach(string argument in new[]{"msbuild",project,"-p:TargetFramework="+framework,
            "-p:UseArtifactsOutput="+artifacts.ToString().ToLowerInvariant(),
            "-p:ArtifactsPath="+Path.Combine(directory.FullName,"artifacts/task-path-test"),"-getProperty:OutputPath,IntermediateOutputPath"})
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout=process.StandardOutput.ReadToEndAsync();
        var stderr=process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch(OperationCanceledException) {
            process.Kill(entireProcessTree:true);
            await process.WaitForExitAsync();
            Assert.Fail("SDK output-path evaluation timed out.");
        }
        string text=await stdout, errors=await stderr;
        Assert.True(process.ExitCode==0,errors+text);
        using var document = System.Text.Json.JsonDocument.Parse(text);
        var properties = document.RootElement.GetProperty("Properties");
        string output = Path.GetFullPath(properties.GetProperty("OutputPath").GetString()!, Path.GetDirectoryName(project)!);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(directory.FullName, "build/tasks", framework))),
            Path.TrimEndingDirectorySeparator(output));
        Assert.Contains(framework,properties.GetProperty("IntermediateOutputPath").GetString());
    }
}
