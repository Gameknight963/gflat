using System;
using System.IO;
using System.Text;

namespace gflat.Projects;

// Shared with the .NET Framework Visual Studio client. Discovery is independent
// of MSBuild and uses APIs available on both hosts.
public static class ProjectSdk
{
    public static bool IsRoot(string directory) =>
        File.Exists(Path.Combine(directory, "build", "Gflat.props")) &&
        File.Exists(Path.Combine(directory, "build", "Gflat.targets")) &&
        File.Exists(Path.Combine(directory, "std", "std.gfproj"));

    public static string? FindRoot(string startingDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startingDirectory)); directory != null; directory = directory.Parent)
        {
            if (IsRoot(directory.FullName)) return directory.FullName;
            string bundled = Path.Combine(directory.FullName, "Sdk");
            if (IsRoot(bundled)) return bundled;
        }
        return null;
    }

    public static string EscapePropertyValue(string value)
    {
        var escaped = new StringBuilder();
        foreach (char character in value)
            if (character is '%' or '$' or '@' or ';' or '?' or '*' or '\'' or '(' or ')')
                escaped.Append('%').Append(((int)character).ToString("X2"));
            else escaped.Append(character);
        return escaped.ToString();
    }
}
