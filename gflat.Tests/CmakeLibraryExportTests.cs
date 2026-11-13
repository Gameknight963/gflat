namespace gflat.Tests;

public sealed class CmakeLibraryExportTests
{
    [Fact]
    [Trait("Optimization", "Independent")]
    public void ProjectBuildRestoresMissingCmakeCompanionsAndCleanRemovesThem()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-cmake-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "answer.gfproj");
            File.WriteAllText(project, TestSupport.ProjectFileFixture.Xml("StaticLibrary"));
            File.WriteAllText(Path.Combine(root, "answer.gf"), "int Answer() => 42;");
            Assert.Equal(0, Program.Main(["build", project, "--no-debug", "--optimization", "2"]));
            string companion = Projects.ProjectModel.Load(project).TargetPath + ".cmake";
            Assert.True(File.Exists(companion));
            File.Delete(companion);
            Assert.Equal(0, Program.Main(["build", project, "--no-debug", "--optimization", "2"]));
            Assert.True(File.Exists(companion));
            Assert.Equal(0, Program.Main(["clean", project]));
            Assert.False(File.Exists(companion));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [Trait("Optimization", "Independent")]
    [InlineData("StaticLibrary")]
    [InlineData("DynamicLibrary")]
    public void RelocatedCmakeImportLinksNativeDependenciesAndDeploysSharedLibraries(string kind)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-cmake-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // The bundled build engine's private paths must not reach CMake's MSBuild.
            Projects.MsBuildHost.Initialize();
            string distribution = Path.Combine(root, "original & library");
            Directory.CreateDirectory(distribution);
            string dependencySource = Path.Combine(root, "dependency.gf");
            File.WriteAllText(dependencySource, "int Dependency() => 40;");
            string dependencyKind = kind == "StaticLibrary" ? "DynamicLibrary" : "StaticLibrary";
            string dependencyName = "dependency" + (OperatingSystem.IsWindows() ? dependencyKind == "StaticLibrary" ? ".lib" : ".dll" : dependencyKind == "StaticLibrary" ? ".a" : ".so");
            string dependency = Path.Combine(distribution, dependencyName);
            Assert.Equal(0, CompilerProgram.Main([dependencySource, "--no-stdlib", "--output-type", dependencyKind, "--optimization", "2", "-o", dependency]));
            string source = Path.Combine(root, "answer.gf");
            File.WriteAllText(source, """
                #lang cpp
                static int Bump(int input) { return input + 1; }
                #lang gflat
                int Answer(ref int input)
                {
                    input = Bump(Dependency() + input);
                    return input;
                }
                """);
            string filename = "answer" + (OperatingSystem.IsWindows() ? kind == "StaticLibrary" ? ".lib" : ".dll" : kind == "StaticLibrary" ? ".a" : ".so");
            string library = Path.Combine(distribution, filename);
            Assert.Equal(0, CompilerProgram.Main([source, "--no-stdlib", "--output-type", kind, "--optimization", "2", "--library-interface", dependency + ".gfi", "-o", library]));
            string export = File.ReadAllText(library + ".cmake");
            Assert.DoesNotContain(distribution.Replace('\\', '/'), export);
            Assert.Contains("Gflat::answer", export);
            string relocated = Path.Combine(root, "relocated & library");
            Directory.Move(distribution, relocated);
            string consumer = Path.Combine(root, "consumer");
            Directory.CreateDirectory(consumer);
            File.WriteAllText(Path.Combine(consumer, "main.cpp"), "#include \"" + filename + ".hpp\"\nint main() { int value = 1; return Answer(value); }");
            File.WriteAllText(Path.Combine(consumer, "CMakeLists.txt"), $$"""
                cmake_minimum_required(VERSION 3.15)
                project(Consumer LANGUAGES CXX)
                include("{{Path.Combine(relocated, filename + ".cmake").Replace('\\', '/')}}")
                add_executable(consumer main.cpp)
                target_link_libraries(consumer PRIVATE Gflat::answer)
                if(MSVC OR CMAKE_CXX_SIMULATE_ID STREQUAL "MSVC")
                  get_target_property(runtime Gflat::answer GFLAT_RUNTIME_LIBRARY)
                  set_property(TARGET consumer PROPERTY MSVC_RUNTIME_LIBRARY "${runtime}")
                endif()
                gflat_deploy_runtime(consumer Gflat::answer)
                """);
            string build = Path.Combine(root, "build");
            var configured = NativeToolchain.Run("cmake", ["-S", consumer, "-B", build, "-DCMAKE_BUILD_TYPE=Release"], 120000);
            Assert.True(configured.ExitCode == 0, configured.StandardOutput + configured.StandardError);
            var compiled = NativeToolchain.Run("cmake", ["--build", build, "--config", "Release", "--parallel", "2"], 120000);
            Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
            string executable = OperatingSystem.IsWindows() ? Path.Combine(build, "Release", "consumer.exe") : Path.Combine(build, "consumer");
            if (OperatingSystem.IsWindows() && !File.Exists(executable)) executable = Path.Combine(build, "consumer.exe");
            if (kind == "DynamicLibrary") Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, filename)));
            if (dependencyKind == "DynamicLibrary") Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, dependencyName)));
            Assert.Equal(42, NativeToolchain.Run(executable, []).ExitCode);
            if (OperatingSystem.IsWindows() && kind == "StaticLibrary")
            {
                File.AppendAllText(Path.Combine(consumer, "CMakeLists.txt"), """

                    add_library(conflicting INTERFACE)
                    set_property(TARGET conflicting PROPERTY INTERFACE_GFLAT_RUNTIME_LIBRARY MultiThreadedDLL)
                    set_property(TARGET conflicting APPEND PROPERTY COMPATIBLE_INTERFACE_STRING GFLAT_RUNTIME_LIBRARY)
                    target_link_libraries(consumer PRIVATE conflicting)
                    """);
                var incompatible = NativeToolchain.Run("cmake", ["-S", consumer, "-B", build], 120000);
                Assert.NotEqual(0, incompatible.ExitCode);
                Assert.Contains("GFLAT_RUNTIME_LIBRARY", incompatible.StandardError);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
