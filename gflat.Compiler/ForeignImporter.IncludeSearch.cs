namespace gflat;

internal static partial class ForeignImporter
{
    private static void TrackIncludeSearchDirectories(Dictionary<string, InputStamp> inputs,
        IReadOnlyList<string> dependencies, IReadOnlyList<string> searchDirectories)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string dependency in dependencies)
            foreach (string owner in searchDirectories)
            {
                string relative = Path.GetRelativePath(owner, Path.GetFullPath(dependency));
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison))
                    continue;
                // A nested include can be replaced by a new header in an earlier search root.
                // Watching only each root's timestamp misses additions in existing subdirectories.
                foreach (string root in searchDirectories)
                {
                    string directory = Path.GetDirectoryName(Path.Combine(root, relative))!;
                    // Creating a missing directory changes its nearest existing parent's timestamp.
                    while (!Directory.Exists(directory) && !string.Equals(directory, root, comparison))
                        directory = Path.GetDirectoryName(directory)!;
                    if (!inputs.ContainsKey(directory)) inputs[directory] = Stamp(directory);
                }
            }
    }
}
