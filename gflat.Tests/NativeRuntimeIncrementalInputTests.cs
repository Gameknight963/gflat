using System.Security;

namespace gflat.Tests;

public sealed class NativeRuntimeIncrementalInputTests
{
    [Fact]
    public void EvaluatedCompileInputsIncludeCAndCppRuntimeSources()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-runtime-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string compilerDirectory = Path.Combine(root, "compiler with spaces");
            string runtime = Path.Combine(compilerDirectory, "Runtime");
            Directory.CreateDirectory(runtime);
            string compiler = Path.Combine(compilerDirectory, "gflat.dll");
            File.WriteAllText(compiler, "fixture only");
            string c = Path.Combine(runtime, "Stdio.c");
            string cpp = Path.Combine(runtime, "Exceptions.cpp");
            File.WriteAllText(c, "C runtime fixture");
            File.WriteAllText(cpp, "C++ runtime fixture");
            string manifest = Path.Combine(root, "runtime-inputs.txt");
            string content = $$"""
                <PropertyGroup>
                  <GflatCompilerPath>{{SecurityElement.Escape(compiler)}}</GflatCompilerPath>
                  <GflatEmitIR>true</GflatEmitIR>
                </PropertyGroup>
                <Target Name="CaptureRuntimeInputs" DependsOnTargets="_GflatPrepareBuild">
                  <WriteLinesToFile File="{{SecurityElement.Escape(manifest)}}" Lines="@(_GflatBuildInputs)" Overwrite="true" />
                </Target>
                """;
            string project = Path.Combine(root, "input.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml(content: content));
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");

            var result = NativeToolchain.Run("dotnet",
                ["msbuild", project, "-nologo", "-v:q", "-t:CaptureRuntimeInputs"], 60000);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError + "\nArtifacts: " + root);
            string[] inputs = File.ReadAllLines(manifest);
            Assert.Contains(inputs, path => Path.GetFullPath(path) == c);
            Assert.Contains(inputs, path => Path.GetFullPath(path) == cpp);
        }
        catch (Exception error)
        {
            error.Data["NativeRuntimeInputDirectory"] = root;
            throw;
        }
        Directory.Delete(root, true);
    }
}
