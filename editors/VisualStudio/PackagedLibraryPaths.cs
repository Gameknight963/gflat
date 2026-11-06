using System;
using System.IO;

namespace gflat.VisualStudio;

internal static class PackagedLibraryPaths
{
    internal static bool Contains(string extensionDirectory, string documentPath)
    {
        string document = Path.GetFullPath(documentPath);
        foreach (string relative in new[] { "Server/Standard", "Compiler/Standard", "Sdk/libraries" })
        {
            string root = Path.GetFullPath(Path.Combine(extensionDirectory, relative))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (document.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
