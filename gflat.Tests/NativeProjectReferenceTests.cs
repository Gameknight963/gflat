using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

[Trait("Optimization", "Independent")]
public sealed class NativeProjectReferenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gflat-native-reference-" + Guid.NewGuid().ToString("N"));
    private string Write(string name, string text)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    private string CppProject(string directory, string kind, string runtime, string references = "", string targetName = "native")
    {
        string engine = VisualStudioMsBuild.FindExecutable();
        var installation = new DirectoryInfo(Path.GetDirectoryName(engine)!);
        while (installation != null && !Directory.Exists(Path.Combine(installation.FullName, "VC", "Tools", "MSVC"))) installation = installation.Parent;
        Assert.True(installation != null, "Visual Studio installation not found for " + engine);
        string toolset = Directory.EnumerateDirectories(Path.Combine(installation.FullName, "MSBuild", "Microsoft", "VC"))
            .SelectMany(path => Directory.Exists(Path.Combine(path, "Platforms", "x64", "PlatformToolsets"))
                ? Directory.EnumerateDirectories(Path.Combine(path, "Platforms", "x64", "PlatformToolsets")) : [])
            .Select(Path.GetFileName).OrderByDescending(name => int.TryParse(name![1..], out var number) ? number : 0).First()!;
        string extra = references.Length == 0 ? "" : "<ItemGroup><ProjectReference Include=\"" + references + "\"/></ItemGroup>";
        string cppBridge = references.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase)
            ? "<Import Project=\"" + Path.Combine(ProjectFileFixture.RepositoryRoot, "build", "Gflat.Cpp.targets") + "\"/>" : "";
        string projectGuid = Guid.NewGuid().ToString("B");
        return Write(directory + "/native.vcxproj", $$"""
            <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup Label="ProjectConfigurations">
                <ProjectConfiguration Include="Debug|x64"><Configuration>Debug</Configuration><Platform>x64</Platform></ProjectConfiguration>
              </ItemGroup>
              <PropertyGroup Label="Globals"><ProjectGuid>{{projectGuid}}</ProjectGuid></PropertyGroup>
              <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props"/>
              <PropertyGroup Label="Configuration"><ConfigurationType>{{kind}}</ConfigurationType><PlatformToolset>{{toolset}}</PlatformToolset></PropertyGroup>
              <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props"/>
              <PropertyGroup><TargetName>{{targetName}}</TargetName><OutDir>$(MSBuildProjectDirectory)\out\</OutDir><IntDir>$(MSBuildProjectDirectory)\obj\</IntDir></PropertyGroup>
              <ItemDefinitionGroup><ClCompile><RuntimeLibrary>{{runtime}}</RuntimeLibrary><PreprocessorDefinitions>NATIVE_BUILD;%(PreprocessorDefinitions)</PreprocessorDefinitions></ClCompile></ItemDefinitionGroup>
              <ItemGroup>
                <ClCompile Include="native.cpp"/>
                <ClCompile Include="excluded.cpp"><ExcludedFromBuild>True</ExcludedFromBuild><RuntimeLibrary>MultiThreaded</RuntimeLibrary></ClCompile>
              </ItemGroup>
              {{extra}}
              <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets"/>
              {{cppBridge}}
            </Project>
            """);
    }

    [Theory]
    [InlineData("StaticLibrary")]
    [InlineData("DynamicLibrary")]
    public void GflatProjectBuildsAndLinksAVisualCppReference(string kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        Write("native/api.h", "#if defined(NATIVE_BUILD)\n#define API __declspec(dllexport)\n#else\n#define API __declspec(dllimport)\n#endif\nAPI int native_answer(int value);\n");
        string references = "";
        if (kind == "DynamicLibrary")
        {
            Write("dependency/native.cpp", "__declspec(dllexport) int dependency_answer(int value) { return value; }");
            CppProject("dependency", "DynamicLibrary", "MultiThreadedDLL", targetName: "dependency");
            references = "../dependency/native.vcxproj";
        }
        Write("native/native.cpp", kind == "DynamicLibrary"
            ? "#include \"api.h\"\n__declspec(dllimport) int dependency_answer(int value);\nint native_answer(int value) { return dependency_answer(value); }"
            : "int native_answer(int value) { return value; }");
        string native = CppProject("native", kind, "MultiThreadedDLL", references);
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: "<ItemGroup><ProjectReference Include=\"../native/native.vcxproj\"/></ItemGroup>"));
        Write("app/main.gf", kind == "DynamicLibrary" ? "#lang cpp\n#include \"../native/api.h\"\n#lang gflat\nint main()=>native_answer(42);" :
            "#lang cpp\nint native_answer(int value);\n#lang gflat\nint main()=>native_answer(42);");
        var graph = ProjectGraph.Load(project);
        Assert.True(graph.RequiresVisualStudioMsBuild);
        Assert.Equal("MultiThreadedDLL", graph.RuntimeLibrary);
        Assert.Empty(graph.BinaryInterfaces);
        Assert.Equal(0, Program.Main(["build", project, "--no-debug"]));
        Assert.Equal(42, NativeToolchain.Run(graph.Root.TargetPath!, []).ExitCode);
        if (kind == "DynamicLibrary")
        {
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(graph.Root.TargetPath)!, "native.dll")));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(graph.Root.TargetPath)!, "dependency.dll")));
        }
    }

    [Theory]
    [InlineData("StaticLibrary")]
    [InlineData("DynamicLibrary")]
    public void VisualCppProjectConsumesAGflatLibraryUsingOrdinaryProjectReferences(string kind)
    {
        if (!OperatingSystem.IsWindows()) return;
        Write("library/answer.gf", "#lang cpp\n#include <string>\nint NativeAnswer() { return (int)std::string(42, 'x').size(); }\n#lang gflat\nnamespace Example { int Answer()=>NativeAnswer(); }");
        Write("library/library.gfproj", kind == "DynamicLibrary"
            ? "<Project Sdk=\"Gflat.Sdk\"><PropertyGroup><OutputType>DynamicLibrary</OutputType><NoStdLib>true</NoStdLib></PropertyGroup></Project>"
            : ProjectFileFixture.Xml(kind));
        string header = "library." + (kind == "DynamicLibrary" ? "dll" : "lib") + ".hpp";
        Write("consumer/native.cpp", "#include \"" + header + "\"\nint main() { return Example::Answer(); }");
        string project = CppProject("consumer", "Application", "MultiThreadedDLL", "../library/library.gfproj");
        var result = VisualStudioMsBuild.Run(project, ["-target:Build", "-p:Configuration=Debug", "-p:Platform=x64", "-verbosity:minimal"]);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal(42, NativeToolchain.Run(Path.Combine(root, "consumer", "out", "native.exe"), []).ExitCode);
    }

    [Fact]
    public void RuntimeDeploymentCrossesAVisualCppProjectBackToAGflatDll()
    {
        if (!OperatingSystem.IsWindows()) return;
        Write("library/answer.gf", "namespace Example { int Answer() => 42; }");
        Write("library/library.gfproj", ProjectFileFixture.Xml("DynamicLibrary"));
        Write("bridge/native.cpp", "#include \"library.dll.hpp\"\n__declspec(dllexport) int ReadAnswer() { return Example::Answer(); }");
        CppProject("bridge", "DynamicLibrary", "MultiThreadedDLL", "../library/library.gfproj");
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content:
            "<ItemGroup><ProjectReference Include=\"../bridge/native.vcxproj\"/></ItemGroup>"));
        Write("app/main.gf", "#lang cpp\n__declspec(dllimport) int ReadAnswer();\n#lang gflat\nint main() => ReadAnswer();");
        Assert.Equal(0, Program.Main(["build", project, "--no-debug"]));
        string output = ProjectModel.Load(project).TargetPath!;
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(output)!, "library.dll")));
        Assert.Equal(42, NativeToolchain.Run(output, []).ExitCode);
    }
}
