using gflat.TestSupport;

namespace gflat.Tests;

[Collection("Project SDK environment")]
public sealed class SourceLibraryProviderBindingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-library-provider-" + Guid.NewGuid().ToString("N"));

    private string Library(bool used)
    {
        string directory = Path.Combine(root, "library");
        Directory.CreateDirectory(directory);
        string project = Path.Combine(directory, "library.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml("SourceLibrary", """
            <Target Name="RecordStandaloneCheck" BeforeTargets="Build;Check">
              <WriteLinesToFile File="standalone-check.txt" Lines="invoked" Overwrite="true" />
            </Target>
            """));
        File.WriteAllText(Path.Combine(directory, "provider.gf"), """
            namespace Api {
                public interface IProvider { static int Read(); }
                public expect alias Provider : IProvider;
                BODY
            }
            """.Replace("BODY", used ? "public int Read()=>Provider::Read();" : ""));
        return project;
    }

    [Theory]
    [InlineData("check", false)]
    [InlineData("check", true)]
    [InlineData("build", false)]
    [InlineData("build", true)]
    public void StandaloneLibraryChecksRequireBindingsEvenForUnusedProviders(string command, bool used)
    {
        string project = Library(used);
        var result = NativeToolchain.Run("dotnet", [typeof(Program).Assembly.Location, command, project]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Expected alias 'Provider' has no implementation; provide replace alias", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void ConsumerSuppliesBindingWithoutBuildingLibrarySeparately()
    {
        string library = Library(used: true);
        string directory = Path.Combine(root, "application");
        Directory.CreateDirectory(directory);
        string project = Path.Combine(directory, "application.gfproj");
        File.WriteAllText(project, ProjectFileFixture.Xml(content: """
            <ItemGroup><ProjectReference Include="../library/library.gfproj" /></ItemGroup>
            """));
        File.WriteAllText(Path.Combine(directory, "main.gf"), """
            static class Answer : Api::IProvider { public static int Read()=>42; }
            replace alias Api::Provider = Answer;
            int main()=>Api::Read();
            """);
        foreach (string[] arguments in new[] { new[] { "check", project }, new[] { "build", project, "--emit-ir" } })
        {
            var result = NativeToolchain.Run("dotnet", [typeof(Program).Assembly.Location, .. arguments]);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(library)!, "standalone-check.txt")));
        }
        Assert.Contains("ret i32 42", File.ReadAllText(Path.Combine(directory, "bin/Debug/application.ll")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
