using System.Xml.Linq;

namespace gflat.TestSupport;

internal static class ProjectFileFixture
{
    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "build", "Gflat.props"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate the repository's gflat build rules.");
        }
    }

    public static string Xml(string kind = "Executable", string content = "") =>
        new XElement("Project", new XAttribute("DefaultTargets", "Build"),
            new XElement("Import", new XAttribute("Project", Path.Combine(RepositoryRoot, "build", "Gflat.props"))),
            new XElement("PropertyGroup", new XElement("GflatProjectKind", kind)),
            XElement.Parse("<Content>" + content + "</Content>").Elements(),
            new XElement("Import", new XAttribute("Project", Path.Combine(RepositoryRoot, "build", "Gflat.targets")))).ToString();
}
