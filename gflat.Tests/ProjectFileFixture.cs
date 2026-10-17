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
        WithImports(new XElement("Project",
            new XElement("PropertyGroup", new XElement("OutputType", kind),
                new XElement("NoStdLib", "true")),
            XElement.Parse("<Content>" + content + "</Content>").Elements()).ToString());

    public static string WithImports(string xml, string? toolchainRoot = null)
    {
        var project = XElement.Parse(xml);
        project.SetAttributeValue("DefaultTargets", "Build");
        project.AddFirst(
            new XElement("PropertyGroup", new XElement("GflatSdkRoot",
                new XAttribute("Condition", "'$(GflatSdkRoot)' == ''"),
                gflat.Projects.ProjectSdk.EscapePropertyValue((toolchainRoot ?? RepositoryRoot).Replace('\\', '/')))),
            new XElement("Import", new XAttribute("Project", "$(GflatSdkRoot)/build/Gflat.props")));
        project.Add(new XElement("Import", new XAttribute("Project", "$(GflatSdkRoot)/build/Gflat.targets")));
        return project.ToString();
    }
}
