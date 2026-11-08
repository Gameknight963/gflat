using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace gflat.Projects;

// Visual C++ combines transitive native outputs in its desktop deployment recipe.
public static class NativeDeploymentRecipe
{
    public static string[] ReadRuntimeFiles(string path)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return XDocument.Load(path).Root!.Elements().Where(element => element.Name.LocalName is "ProjectOutputs" or "ContentFiles" or "SatelliteDlls")
            .SelectMany(element => element.Descendants().Where(child => child.Name.LocalName == "FullPath"))
            .Select(element => element.Value)
            .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(directory, file)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
