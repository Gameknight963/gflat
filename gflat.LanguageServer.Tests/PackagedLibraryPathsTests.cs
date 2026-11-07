using gflat.VisualStudio;

namespace gflat.LanguageServer.Tests;

public sealed class PackagedLibraryPathsTests
{
    [Fact]
    public void PublishedRootsUseTheSameDirectoryBoundariesAsBufferProtection()
    {
        string extension = Path.GetFullPath("extension-installation");
        var roots = PackagedLibraryPaths.Roots(extension);
        Assert.Equal(3, roots.Length);
        foreach (string root in roots)
        {
            Assert.True(Path.IsPathRooted(root));
            Assert.Equal(Path.DirectorySeparatorChar, root[^1]);
            Assert.True(PackagedLibraryPaths.Contains(extension, Path.Combine(root, "Source.gf")));
            Assert.False(PackagedLibraryPaths.Contains(extension, root.TrimEnd(Path.DirectorySeparatorChar) + "-copy/Source.gf"));
        }
    }

    [Theory]
    [InlineData("Server/Standard/core/Text/String.gf", true)]
    [InlineData("Compiler/Standard/core/Text/String.gf", true)]
    [InlineData("Sdk/libraries/platform/Windows/File.gf", true)]
    [InlineData("Server/Standard-copy/String.gf", false)]
    [InlineData("Server/Standard/../../../workspace/Standard/String.gf", false)]
    [InlineData("workspace/Standard/String.gf", false)]
    public void OnlyBundledLibraryRootsAreProtected(string relative, bool expected)
    {
        string extension = Path.GetFullPath("extension-installation");
        Assert.Equal(expected, PackagedLibraryPaths.Contains(extension, Path.Combine(extension, relative)));
    }
}
