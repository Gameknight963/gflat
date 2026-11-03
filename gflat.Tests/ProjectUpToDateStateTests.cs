using gflat.Projects;
using Microsoft.Build.Evaluation;

namespace gflat.Tests;

public sealed class ProjectUpToDateStateTests
{
    [Fact]
    public void SourceLibraryStateTracksGlobAdditionsRemovalsImportsAndHeaderCandidates()
    {
        WithProject((directory, project, globals) =>
        {
            string source = Path.Combine(directory, "one.gf"), header = Path.Combine(directory, "header.gfh");
            string dependencies = Path.Combine(directory, "app.includes");
            File.WriteAllText(source, "struct One {}");
            File.WriteAllText(dependencies, header + Environment.NewLine);
            string state = Path.Combine(directory, "state.xml");
            Save();
            Assert.True(Current());
            // Missing include candidates are part of the snapshot. Creating a
            // higher-priority header changes which file the preprocessor reads.
            File.WriteAllText(header, "#define VALUE 1");
            Assert.False(Current());
            Save();
            Assert.True(Current());
            File.AppendAllText(header, "\n#define OTHER 2");
            Assert.False(Current());
            Save();
            File.WriteAllText(Path.Combine(directory, "two.gf"), "struct Two {}");
            Assert.False(Current());
            Save();
            File.Delete(source);
            Assert.False(Current());
            Save();
            File.WriteAllText(Path.Combine(directory, "later.props"), "<Project><PropertyGroup><Extra>true</Extra></PropertyGroup></Project>");
            Assert.False(Current());

            void Save() => BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            bool Current() => BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None);
        });
    }

    [Fact]
    public void StateTracksReferenceSourcesCompilerFilesAndUnsavedProjectXml()
    {
        WithProject((directory, project, globals) =>
        {
            string library = Path.Combine(directory, "library.gfproj");
            File.WriteAllText(library, "<Project><PropertyGroup><OutputType>SourceLibrary</OutputType></PropertyGroup><ItemGroup><Compile Include=\"library/*.gf\" /></ItemGroup></Project>");
            Directory.CreateDirectory(Path.Combine(directory, "library"));
            string source = Path.Combine(directory, "library", "main.gf");
            File.WriteAllText(source, "struct Item {}");
            File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", "<ItemGroup><ProjectReference Include=\"library.gfproj\" /></ItemGroup></Project>"));
            string state = Path.Combine(directory, "state.xml");
            Save();
            Assert.True(Current());
            File.AppendAllText(source, "\nstruct Other {}");
            Assert.False(Current());
            Save();
            File.WriteAllText(Path.Combine(directory, "compiler.dll"), "new compiler dependency");
            Assert.False(Current());
            Save();
            Assert.False(BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals) + " ", CancellationToken.None));

            void Save() => BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            bool Current() => BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None);
        });
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void NativeOutputLossReplacementAndEditsDuringBuildInvalidateState(string emitIr)
    {
        WithProject((directory, project, globals) =>
        {
            globals["OutputType"] = "Executable";
            globals["GflatEmitIR"] = emitIr;
            globals["GflatIROutput"] = Path.Combine(directory, "app.ll");
            string output = globals["GflatIROutput"], state = Path.Combine(directory, "state.xml");
            string source = Path.Combine(directory, "main.gf");
            File.WriteAllText(source, "int main() => 0;");
            File.WriteAllText(output, "first output");
            Save();
            Assert.True(Current());
            File.Delete(output);
            Assert.False(Current());
            File.WriteAllText(output, "other configuration's output");
            Assert.False(Current());
            Save();
            File.Delete(state);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(10));
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.False(File.Exists(state));

            void Save() => BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            bool Current() => BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None);
        });
    }

    [Fact]
    public void SourceLibrariesCanCacheLogicalLibrariesAndUnsupportedReferenceContextsStayUncached()
    {
        WithProject((directory, project, globals) =>
        {
            globals["OutputType"] = "sourcelibrary";
            File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", "<ItemGroup><NativeLibrary Include=\"kernel32\" /></ItemGroup></Project>"));
            string state = Path.Combine(directory, "state.xml");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.True(File.Exists(state));
            File.Delete(state);
            globals["OutputType"] = "Executable";
            globals["GflatClang"] = Path.Combine(directory, "compiler.exe");
            globals["TargetPath"] = Path.Combine(directory, "app.exe");
            File.WriteAllText(globals["TargetPath"], "output");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.False(File.Exists(state));
            globals["GflatEmitIR"] = "true";
            globals["GflatIROutput"] = globals["TargetPath"];
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.True(File.Exists(state));
            File.Delete(state);
            globals["GflatEmitIR"] = "false";
            globals["GflatNativeLinkOverrides"] = Path.Combine(directory, "overrides.xml");
            File.WriteAllText(globals["GflatNativeLinkOverrides"], "<NativeLinkInputs />");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.False(File.Exists(state));
            globals["OutputType"] = "SourceLibrary";
            string library = Path.Combine(directory, "library.gfproj");
            File.WriteAllText(library, "<Project />");
            File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>", "<ItemGroup><ProjectReference Include=\"library.gfproj\"><AdditionalProperties>Custom=true</AdditionalProperties></ProjectReference></ItemGroup></Project>"));
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.False(File.Exists(state));
        });
    }

    [Fact]
    public void UppercaseDebugFlagStillRequiresThePdbOutput()
    {
        if (!OperatingSystem.IsWindows()) return;
        WithProject((directory, project, globals) =>
        {
            globals["OutputType"] = "executable";
            globals["DebugSymbols"] = "TRUE";
            globals["GflatClang"] = Path.Combine(directory, "compiler.exe");
            globals["TargetPath"] = Path.Combine(directory, "app.exe");
            File.WriteAllText(globals["TargetPath"], "program");
            string state = Path.Combine(directory, "state.xml"), pdb = Path.ChangeExtension(globals["TargetPath"], ".pdb");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.False(File.Exists(state));
            File.WriteAllText(pdb, "symbols");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            Assert.True(BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None));
            File.Delete(pdb);
            Assert.False(BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None));
        });
    }

    [Fact]
    public void MalformedSnapshotsAndChangedRequestGlobalsFallBackToBuilding()
    {
        WithProject((directory, project, globals) =>
        {
            string state = Path.Combine(directory, "state.xml");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            string valid = File.ReadAllText(state), xml = Xml(project, globals);
            Assert.True(BuildUpToDateState.IsUpToDate(state, project, xml, CancellationToken.None, globals));
            Assert.False(BuildUpToDateState.IsUpToDate(state, project, xml, CancellationToken.None,
                new Dictionary<string, string> { ["Configuration"] = "Release" }));
            foreach (string malformed in new[]
            {
                "<GflatBuildState",
                "<GflatBuildState Version=\"1\"><ProjectXml>bad base64!</ProjectXml><Globals /></GflatBuildState>",
                valid.Replace("<Globals>", "<Globals><Property Name=\"Configuration\" Value=\"Debug\" />"),
                "<GflatBuildState Version=\"1\"><ProjectXml /><Globals><Property /></Globals></GflatBuildState>"
            })
            {
                File.WriteAllText(state, malformed);
                Assert.False(BuildUpToDateState.IsUpToDate(state, project, xml, CancellationToken.None));
            }
        });
    }

    [Fact]
    public void FailedPostBuildTargetsInvalidateEveryConfigurationUntilAnotherSuccessfulBuild()
    {
        WithProject((directory, project, globals) =>
        {
            string debug = Path.Combine(directory, "debug.xml"), release = Path.Combine(directory, "release.xml");
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(debug);
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(release);
            DateTime failed = DateTime.UtcNow.AddSeconds(1);
            BuildUpToDateState.RecordBuildFailure(project, failed);
            Assert.False(BuildUpToDateState.IsUpToDate(debug, project, Xml(project, globals), CancellationToken.None));
            Assert.False(BuildUpToDateState.IsUpToDate(release, project, Xml(project, globals), CancellationToken.None));
            string marker = Path.Combine(directory, ".vs", "gflat", "app.gfproj.build.failed");
            Assert.True(File.Exists(marker));
            // A later success supersedes the failure. The other configuration
            // still has its older snapshot and must rebuild independently.
            BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(debug);
            File.SetLastWriteTimeUtc(debug, failed.AddSeconds(1));
            Assert.True(BuildUpToDateState.IsUpToDate(debug, project, Xml(project, globals), CancellationToken.None));
            Assert.False(BuildUpToDateState.IsUpToDate(release, project, Xml(project, globals), CancellationToken.None));
        });
    }

    [Fact]
    public void DeclaredInputsAndOutputsSupportCustomBuildTargets()
    {
        WithProject((directory, project, globals) =>
        {
            File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>",
                "<ItemGroup><UpToDateCheckInput Include=\"config.txt\" /><UpToDateCheckOutput Include=\"generated.bin\" /></ItemGroup></Project>"));
            string input = Path.Combine(directory, "config.txt"), output = Path.Combine(directory, "generated.bin");
            string state = Path.Combine(directory, "state.xml");
            File.WriteAllText(input, "one");
            File.WriteAllText(output, "generated");
            Save();
            Assert.True(Current());
            File.AppendAllText(input, "two");
            Assert.False(Current());
            Save();
            File.Delete(output);
            Assert.False(Current());
            File.Delete(state);
            Save();
            Assert.False(File.Exists(state));
            File.WriteAllText(output, "generated again");
            File.Delete(input);
            Save();
            Assert.False(File.Exists(state));

            void Save() => BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            bool Current() => BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None);
        });
    }

    [Fact]
    public void RelativeSdkDefaultsStayLocalToEachProjectInTheReferenceGraph()
    {
        WithProject((directory, _, _) =>
        {
            string appDirectory = Path.Combine(directory, "apps", "app"), libraryDirectory = Path.Combine(directory, "Standard");
            string buildDirectory = Path.Combine(directory, "build");
            Directory.CreateDirectory(appDirectory);
            Directory.CreateDirectory(libraryDirectory);
            Directory.CreateDirectory(buildDirectory);
            string project = Path.Combine(appDirectory, "app.gfproj"), compiler = Path.Combine(appDirectory, "compiler.exe");
            File.WriteAllText(compiler, "compiler");
            File.WriteAllText(Path.Combine(appDirectory, "main.gf"), "struct App {}");
            string librarySource = Path.Combine(libraryDirectory, "item.gf");
            File.WriteAllText(librarySource, "struct Item {}");
            File.WriteAllText(Path.Combine(buildDirectory, "app.props"), "<Project><ItemGroup><Compile Include=\"main.gf\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(buildDirectory, "library.props"), "<Project><ItemGroup><Compile Include=\"item.gf\" /></ItemGroup></Project>");
            File.WriteAllText(project, """
                <Project>
                  <PropertyGroup>
                    <OutputType>SourceLibrary</OutputType>
                    <GflatSdkRoot>../..</GflatSdkRoot>
                    <GflatCompilerPath>$(MSBuildProjectDirectory)/compiler.exe</GflatCompilerPath>
                    <GflatBundledCompiler>$(GflatCompilerPath)</GflatBundledCompiler>
                    <GflatCompilerCommand>test-compiler</GflatCompilerCommand>
                    <_GflatKnownCompilerCommand>test-compiler</_GflatKnownCompilerCommand>
                  </PropertyGroup>
                  <Import Project="$(GflatSdkRoot)/build/app.props" />
                  <ItemGroup><ProjectReference Include="../../Standard/Standard.gfproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Standard.gfproj"), """
                <Project>
                  <PropertyGroup><OutputType>SourceLibrary</OutputType><GflatSdkRoot>..</GflatSdkRoot></PropertyGroup>
                  <Import Project="$(GflatSdkRoot)/build/library.props" />
                </Project>
                """);
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { ["Configuration"] = "Debug", ["Platform"] = "AnyCPU" };
            var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { ["GflatSdkRoot"] = "../..", ["GflatCompilerPath"] = Path.Combine(appDirectory, "compiler.exe"), ["GflatBundledCompiler"] = Path.Combine(appDirectory, "compiler.exe") };
            // MSBuild preserves the explicit separator spelling of this local
            // property. Compare CPS's evaluated value rather than normalizing it.
            using (var collection = new ProjectCollection())
            {
                var evaluated = new Project(project, globals, null, collection);
                foreach (string name in required.Keys.ToArray()) required[name] = evaluated.GetPropertyValue(name);
            }
            string state = Path.Combine(appDirectory, "state.xml");
            Save();
            Assert.True(Current(required));
            Assert.DoesNotContain("GflatSdkRoot", BuildUpToDateState.RecordedGlobalPropertyNames(state));
            required["GflatSdkRoot"] = "elsewhere";
            Assert.False(Current(required));
            Assert.False(Current(new Dictionary<string, string> { ["UnrecordedCustomGlobal"] = "" }));
            required["GflatSdkRoot"] = "../..";
            File.AppendAllText(librarySource, "\nstruct More {}");
            Assert.False(Current(required));

            void Save() => BuildUpToDateState.Capture(project, globals, DateTime.UtcNow).Write(state);
            bool Current(Dictionary<string, string> expected) => BuildUpToDateState.IsUpToDate(state, project, Xml(project, globals), CancellationToken.None, expected);
        });
    }

    private static string Xml(string project, Dictionary<string, string> globals)
    {
        using var collection = new ProjectCollection();
        return new Project(project, globals, null, collection).Xml.RawXml;
    }

    private static void WithProject(Action<string, string, Dictionary<string, string>> action)
    {
        MsBuildHost.Initialize();
        string directory = Path.Combine(Path.GetTempPath(), "gflat-up-to-date-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string project = Path.Combine(directory, "app.gfproj");
        File.WriteAllText(Path.Combine(directory, "compiler.exe"), "compiler");
        File.WriteAllText(project, "<Project>\r\n<PropertyGroup><OutputType>SourceLibrary</OutputType><_GflatIncludeDependencies>$(MSBuildProjectDirectory)/app.includes</_GflatIncludeDependencies></PropertyGroup>\r\n<ItemGroup><Compile Include=\"*.gf\" /></ItemGroup><Import Project=\"later.props\" Condition=\"Exists('later.props')\" />\r\n</Project>");
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GflatCompilerPath"] = Path.Combine(directory, "compiler.exe"),
            ["GflatCompilerCommand"] = "test-compiler",
            ["_GflatKnownCompilerCommand"] = "test-compiler",
            ["Configuration"] = "Debug",
            ["Platform"] = "AnyCPU"
        };
        try { action(directory, project, globals); }
        finally { Directory.Delete(directory, true); }
    }
}
