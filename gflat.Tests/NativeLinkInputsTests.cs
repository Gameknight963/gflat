using gflat.Projects;

namespace gflat.Tests;

public sealed class NativeLinkInputsTests
{
    [Fact]
    public void LinkArgumentsKeepDirectoriesFirstAndPreserveLibraryOrderAndRepeats()
    {
        InDirectory(root => {
            string file = Path.Combine(root, "native & semi; space", "answer.o");
            string directory = Path.GetDirectoryName(file)!;
            var inputs = NativeLinkInputs.Create([
                new("Name", "owner"), new("File", file), new("Name", "dependency"), new("Name", "owner")
            ], [directory], root);
            var arguments = new List<string> { "program.ll", "-o", "program.exe" };
            NativeToolchain.AddNativeLibraries(arguments, inputs);
            Assert.Equal(new[] { "program.ll", "-o", "program.exe", "-L" + directory, "-lowner", file, "-ldependency", "-lowner" }, arguments);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-pthread")]
    [InlineData("name/path")]
    [InlineData("name\\path")]
    [InlineData("name;other")]
    [InlineData("name\nother")]
    [InlineData("name\0other")]
    public void LogicalNamesCannotBecomePathsOrDriverOptions(string name)
        => Assert.Throws<ArgumentException>(() => new NativeLibraryInput("Name", name));

    [Fact]
    public void ManifestRoundTripNormalizesPathsAndDoesNotRewriteUnchangedContent()
    {
        InDirectory(root => {
            string relative = Path.Combine("native & semi; space", "answer.o");
            var inputs = NativeLinkInputs.Create([new("File", relative), new("Name", "answer"), new("File", relative)],
                ["native & semi; space", Path.Combine("native & semi; space", ".")], root);
            Assert.Equal(3, inputs.Libraries.Count);
            Assert.Single(inputs.Directories);
            Assert.Equal(Path.Combine(root, relative), inputs.Libraries[0].Value);
            string manifest = Path.Combine(root, "link inputs.xml");
            inputs.Write(manifest);
            File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow.AddDays(-2));
            DateTime written = File.GetLastWriteTimeUtc(manifest);
            inputs.Write(manifest);
            Assert.Equal(written, File.GetLastWriteTimeUtc(manifest));
            var copy = NativeLinkInputs.Read(manifest);
            Assert.Equal(inputs.Libraries.Select(i => (i.Kind, i.Value)), copy.Libraries.Select(i => (i.Kind, i.Value)));
            Assert.Equal(inputs.Directories, copy.Directories);
        });
    }

    [Theory]
    [InlineData("<NativeLinkInputs Version='2'><Libraries/><Directories/></NativeLinkInputs>")]
    [InlineData("<NativeLinkInputs Version='1'><Libraries><Library Kind='File' Value='input.o' Extra='unsupported'/></Libraries><Directories/></NativeLinkInputs>")]
    [InlineData("<NativeLinkInputs Version='1'><Libraries/><Directories/><Unexpected/></NativeLinkInputs>")]
    [InlineData("<NativeLinkInputs Version='1'><Directories/><Libraries/></NativeLinkInputs>")]
    public void ManifestRejectsUnsupportedSchema(string xml)
    {
        InDirectory(root => {
            string manifest = Path.Combine(root, "inputs.xml"); File.WriteAllText(manifest, xml);
            Assert.Throws<InvalidDataException>(() => NativeLinkInputs.Read(manifest));
        });
    }

    [Fact]
    public void TransitiveDiamondKeepsOwnersBeforeSharedDependenciesAndPreservesItemOrder()
    {
        InDirectory(root => {
            foreach (string name in new[] { "left", "right", "shared" }) Directory.CreateDirectory(Path.Combine(root, name));
            string WriteProject(string name, string items, string kind = "SourceLibrary") {
                string path = Path.Combine(root, name, name + ".gfproj");
                File.WriteAllText(path, TestSupport.ProjectFileFixture.Xml(kind, "<ItemGroup>" + items + "</ItemGroup>"));
                return path;
            }
            WriteProject("shared", "<NativeLibrary Include='dependency'/>");
            WriteProject("left", "<NativeLibrary Include='left'/><NativeLibraryFile Include='owner.o'/><NativeLibrary Include='left'/><NativeLibrary Include='removed'/><NativeLibrary Remove='removed'/><ProjectReference Include='../shared/shared.gfproj'/>");
            WriteProject("right", "<nativeLibrary Include='right'/><ProjectReference Include='../shared/shared.gfproj'/><NativeLibrary Include='debug_only' Condition=\"'$(Configuration)' == 'Debug'\"/><NativeLibrary Include='release_only' Condition=\"'$(Configuration)' == 'Release'\"/>");
            string app = Path.Combine(root, "app.gfproj");
            File.WriteAllText(app, TestSupport.ProjectFileFixture.Xml(content: "<ItemGroup><NativeLibrary Include='app'/><ProjectReference Include='left/left.gfproj'/><ProjectReference Include='right/right.gfproj'/></ItemGroup>"));
            var inputs = ProjectGraph.Load(app).NativeLinkInputs;
            Assert.Equal(new[] { "app", "left", Path.Combine(root, "left", "owner.o"), "left", "right", "debug_only", "dependency" }, inputs.Libraries.Select(library => library.Value));
        });
    }

    [Fact]
    public void ManifestRejectsUnknownInputKinds()
    {
        InDirectory(root => {
            string manifest = Path.Combine(root, "inputs.xml");
            File.WriteAllText(manifest, "<NativeLinkInputs Version='1'><Libraries><Library Kind='Option' Value='-pthread'/></Libraries><Directories/></NativeLinkInputs>");
            Assert.Throws<ArgumentException>(() => NativeLinkInputs.Read(manifest));
        });
    }

    private static void InDirectory(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-native-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); } finally { Directory.Delete(root, true); }
    }
}
