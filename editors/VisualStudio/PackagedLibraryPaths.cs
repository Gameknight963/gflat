using System;
using System.IO;

namespace gflat.VisualStudio;

internal static class PackagedLibraryPaths
{
    internal static string[] Roots(string extensionDirectory)
    {
        var roots = new[] { "Server/Standard", "Compiler/Standard", "Sdk/libraries" };
        for (int index = 0; index < roots.Length; index++)
            roots[index] = Path.GetFullPath(Path.Combine(extensionDirectory, roots[index]))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return roots;
    }

    internal static bool Contains(string extensionDirectory, string documentPath)
    {
        string document = Path.GetFullPath(documentPath);
        foreach (string root in Roots(extensionDirectory))
        {
            if (document.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
