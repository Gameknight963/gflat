using System.Xml.Linq;
using System.Runtime.CompilerServices;

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

    [ModuleInitializer]
    internal static void ConfigureSdkHost()
    {
        // Only this test host and its children use the checkout SDK. Nothing is
        // installed into .NET/Visual Studio or persisted in the user's environment.
        Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(RepositoryRoot, "build", "Sdks"));
    }

    public static string Xml(string kind = "Executable", string content = "") =>
        new XElement("Project", new XAttribute("Sdk", "Gflat.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", kind),
                new XElement("NoStdLib", "true")),
            XElement.Parse("<Content>" + content + "</Content>").Elements()).ToString();
}
