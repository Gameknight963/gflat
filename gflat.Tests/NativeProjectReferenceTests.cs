using gflat.Projects;
using gflat.TestSupport;

namespace gflat.Tests;

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

    private string CppProject(string directory, string kind, string runtime, string references = "")
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
        return Write(directory + "/native.vcxproj", $$"""
            <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup Label="ProjectConfigurations">
                <ProjectConfiguration Include="Debug|x64"><Configuration>Debug</Configuration><Platform>x64</Platform></ProjectConfiguration>
              </ItemGroup>
              <PropertyGroup Label="Globals"><ProjectGuid>{F13633CC-C161-4740-8A94-EA09C794D013}</ProjectGuid></PropertyGroup>
              <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props"/>
              <PropertyGroup Label="Configuration"><ConfigurationType>{{kind}}</ConfigurationType><PlatformToolset>{{toolset}}</PlatformToolset></PropertyGroup>
              <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props"/>
              <PropertyGroup><OutDir>$(MSBuildProjectDirectory)\out\</OutDir><IntDir>$(MSBuildProjectDirectory)\obj\</IntDir></PropertyGroup>
              <ItemDefinitionGroup><ClCompile><RuntimeLibrary>{{runtime}}</RuntimeLibrary><PreprocessorDefinitions>NATIVE_BUILD;%(PreprocessorDefinitions)</PreprocessorDefinitions></ClCompile></ItemDefinitionGroup>
              <ItemGroup><ClCompile Include="native.cpp"/></ItemGroup>
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
        Write("native/native.cpp", kind == "DynamicLibrary" ? "#include \"api.h\"\nint native_answer(int value) { return value; }" : "int native_answer(int value) { return value; }");
        string native = CppProject("native", kind, "MultiThreadedDLL");
        string project = Write("app/app.gfproj", ProjectFileFixture.Xml(content: "<ItemGroup><ProjectReference Include=\"../native/native.vcxproj\"/></ItemGroup>"));
        Write("app/main.gf", kind == "DynamicLibrary" ? "#lang cpp\n#include \"../native/api.h\"\n#lang gflat\nint main()=>native_answer(42);" :
            "#lang cpp\nint native_answer(int value);\n#lang gflat\nint main()=>native_answer(42);");
        var graph = ProjectGraph.Load(project);
        Assert.True(graph.RequiresVisualStudioMsBuild);
        Assert.Equal("MultiThreadedDLL", graph.RuntimeLibrary);
        Assert.Empty(graph.BinaryInterfaces);
        Assert.Equal(0, Program.Main(["build", project, "--no-debug"]));
        Assert.Equal(42, NativeToolchain.Run(graph.Root.TargetPath!, []).ExitCode);
        if (kind == "DynamicLibrary") Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(graph.Root.TargetPath)!, "native.dll")));
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
}
