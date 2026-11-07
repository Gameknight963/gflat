using gflat.VisualStudio;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class SourceFileTemplateTests
{
    [Theory]
    [InlineData("Player.gf", "class Player")]
    [InlineData("HashSet2.gf", "class HashSet2")]
    [InlineData("player.gf", null)]
    [InlineData("integer_parsing.gf", null)]
    [InlineData("Player_Tests.gf", null)]
    [InlineData("Player.Tests.gf", null)]
    [InlineData("1Player.gf", null)]
    public void PascalCaseFilesCreateMatchingTypesAndEveryFileUsesItsFolders(string file, string? declaration)
    {
        string source = SourceFileTemplate.Generate(file, "Game", "World/Actors", true, true, true);
        Assert.StartsWith("namespace Game::World::Actors\r\n{\r\n", source);
        if (declaration == null) Assert.DoesNotContain("class ", source);
        else Assert.Contains(declaration, source);
        CheckSource(source);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ClassAndNamespaceGenerationAreIndependentlyConfigurable(bool type, bool ns)
    {
        string source = SourceFileTemplate.Generate("Player.gf", "Game", "Actors", type, ns, true);
        Assert.Equal(type, source.Contains("class Player"));
        Assert.Equal(ns, source.Contains("namespace Game::Actors"));
        CheckSource(source);
    }

    [Theory]
    [InlineData("Company.Game", "Company::Game")]
    [InlineData("Company::Game", "Company::Game")]
    [InlineData("class", "@class")]
    [InlineData("My Game", "My_Game")]
    [InlineData("123", "_123")]
    [InlineData("", "")]
    public void NamespacePrefixesAndKeywordFoldersProduceValidSource(string prefix, string expected)
    {
        string source = SourceFileTemplate.Generate("Player.gf", prefix, "class/int", true, true, true);
        Assert.Contains("namespace " + (expected.Length > 0 ? expected + "::" : "") + "@class::@int", source);
        CheckSource(source);
    }

    [Fact]
    public void ProjectRootAndCustomSourceRootsDoNotLeakParentDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-item-template");
        string project = Path.Combine(root, "Game.gfproj");
        string file = Path.Combine(root, "src", "World", "Player.gf");
        Assert.Equal(Path.Combine("src", "World"), SourceFileTemplate.RelativeFolder(project, file, ""));
        Assert.Equal("World", SourceFileTemplate.RelativeFolder(project, file, "src"));
        Assert.Equal("World", SourceFileTemplate.RelativeFolder(project, file, Path.Combine(root, "src")));
        Assert.Equal("", SourceFileTemplate.RelativeFolder(project, Path.Combine(root, "Player.gf"), ""));
        Assert.Equal("", SourceFileTemplate.RelativeFolder(project, Path.Combine(root + "-other", "Player.gf"), ""));
    }

    [Fact]
    public void GeneratedBracesRespectTheFormattingOption()
    {
        string source = SourceFileTemplate.Generate("Player.gf", "Game", "", true, true, false);
        Assert.Equal("namespace Game {\r\n    class Player {\r\n    }\r\n}\r\n", source);
        CheckSource(source);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("  ")]
    public void NewFilesUseTheEditorsIndentation(string indentation)
    {
        string source = SourceFileTemplate.Generate("Player.gf", "Game", "", true, true, true, indentation);
        Assert.Contains("\r\n" + indentation + "class Player\r\n" + indentation + "{\r\n" + indentation + "}", source);
        CheckSource(source);
    }

    private static void CheckSource(string text)
    {
        var diagnostics = new DiagnosticBag();
        Compiler.Check([new SourceFile(Path.GetFullPath("generated-item.gf"), text)], diagnostics,
            new() { NoStdLib = true, NoBuiltins = true });
        Assert.DoesNotContain(diagnostics.Items, d => d.Severity == DiagnosticSeverity.Error);
    }
}
