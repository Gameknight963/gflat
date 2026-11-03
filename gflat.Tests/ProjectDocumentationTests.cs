using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using gflat.Projects;

namespace gflat.Tests;

public sealed class ProjectDocumentationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CompleteProjectExamplesCheckWithoutToolchainEnvironment(int example)
    {
        string document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Guides", "PROJECTS.md"));
        string[] projects = Regex.Matches(document, @"(?ms)^```xml\r?\n(?<xml>.*?)^```")
            .Select(match => (Text: match.Groups["xml"].Value, Project: XElement.Parse(match.Groups["xml"].Value)))
            .Where(item => item.Project.Name == "Project"
                && ((string?)item.Project.Attribute("Sdk") == "Gflat.Sdk"
                    || item.Project.Elements("Import").Any(import =>
                        (string?)import.Attribute("Project") == "$(GflatSdkRoot)/build/Gflat.props")))
            .Where(item => !item.Project.Elements("ItemGroup").Elements("NativeLibraryFile").Any())
            .Select(item => item.Text)
            .ToArray();
        Assert.Equal(4, projects.Length);

        string directory = Path.Combine(Path.GetTempPath(), "gflat documented project " + Guid.NewGuid().ToString("N"));
        string app = Path.Combine(directory, "Application");
        Directory.CreateDirectory(app);
        try
        {
            string path = Path.Combine(app, "Application.gfproj");
            File.WriteAllText(path, BindFallbackPath(projects[example], app));
            var xml = XElement.Parse(projects[example]);
            bool referencesLibrary = xml.Elements("ItemGroup").Elements("ProjectReference").Any();
            bool sourceLibrary = xml.Elements("PropertyGroup").Elements("OutputType")
                .Any(property => property.Value == "SourceLibrary");
            File.WriteAllText(Path.Combine(app, "main.gf"), sourceLibrary ? "int Answer() => 7;"
                : referencesLibrary ? "int main() => Answer() - 7;" : "int main() => 0;");

            if (referencesLibrary)
            {
                string library = Path.Combine(directory, "Utilities");
                Directory.CreateDirectory(library);
                string libraryXml = projects.Single(project => XElement.Parse(project)
                    .Elements("PropertyGroup").Elements("OutputType")
                    .Any(property => property.Value == "SourceLibrary"));
                File.WriteAllText(Path.Combine(library, "Utilities.gfproj"), BindFallbackPath(libraryXml, library));
                File.WriteAllText(Path.Combine(library, "main.gf"), "int Answer() => 7;");
                string scratch = Path.Combine(app, "scratch");
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "excluded.gf"), "this must not be compiled");
            }

            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = app,
            };
            foreach (string argument in new[] { typeof(Program).Assembly.Location, "check", path, "--no-stdlib" })
                start.ArgumentList.Add(argument);
            start.Environment.Remove("GflatSdkRoot");
            start.Environment.Remove("GflatDefaultSdkRoot");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Checking the documented project exceeded 60 seconds.");
            }
            Assert.True(process.ExitCode == 0, $"Documented project {example}:\n{await stdout}\n{await stderr}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string BindFallbackPath(string xml, string directory)
    {
        string relativeRoot = Path.GetRelativePath(directory, TestSupport.ProjectFileFixture.RepositoryRoot)
            .Replace('\\', '/');
        string propertyValue = ProjectSdk.EscapePropertyValue(relativeRoot);
        string xmlValue = System.Security.SecurityElement.Escape(propertyValue)!;
        return xml.Replace("../path/to/gflat", xmlValue, StringComparison.Ordinal);
    }
}
