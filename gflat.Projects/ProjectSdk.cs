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
        File.Exists(Path.Combine(directory, "build", "Gflat.targets"));

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

    /// <summary>Install the SDK into an MSBuild host's documented Sdks directory.
    /// This is an explicit setup action, never performed during project evaluation.</summary>
    public static string Install(string toolchainRoot, string sdkDirectory)
    {
        toolchainRoot = Path.GetFullPath(toolchainRoot);
        if (!IsRoot(toolchainRoot)) throw new ArgumentException("The toolchain must contain build/Gflat.props and build/Gflat.targets.");
        string destination = Path.Combine(Path.GetFullPath(sdkDirectory), "Gflat.Sdk", "Sdk");
        Directory.CreateDirectory(destination);
        string root = EscapePropertyValue(toolchainRoot.Replace('\\', '/'));
        string escapedRoot = System.Security.SecurityElement.Escape(root)!;
        File.WriteAllText(Path.Combine(destination, "Sdk.props"),
            "<Project><PropertyGroup><GflatSdkRoot Condition=\"'$(GflatSdkRoot)' == ''\">" + escapedRoot +
            "</GflatSdkRoot></PropertyGroup><Import Project=\"$(GflatSdkRoot)/build/Gflat.props\" /></Project>\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(destination, "Sdk.targets"),
            "<Project><Import Project=\"$(GflatSdkRoot)/build/Gflat.targets\" /></Project>\n", new UTF8Encoding(false));
        return destination;
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
